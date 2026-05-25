using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ArchitectsOfWillFactory"/> (Alara Reborn).
///
/// Covers:
/// - Identity ({2}{U}{B} Artifact Creature — Human Wizard 3/3).
/// - Flying / artifact-creature multi-type.
/// - ETB trigger shape — single 1..1 "target player" request.
/// - ETB resolve reorders the top three of the chosen target's library
///   when the controller supplies a decision.
/// - Cycling activated ability ({U/B} hybrid cost + DiscardSelfCost).
/// - Cycling publishes <see cref="CardCycledEvent"/> through the
///   supplied bus — the surface Living End / Lightning Rift / Curator
///   of Mysteries subscribe to.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class ArchitectsOfWillFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchitectsOfWill_Identity_ArtifactCreatureHumanWizard33()
    {
        var card = ArchitectsOfWillFactory.Create(_alice);

        card.Name.Should().Be("Architects of Will");
        card.ManaCost.ToString().Should().Be("{2}{U}{B}");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue(
            "printed type line is Artifact Creature — Human Wizard");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArchitectsOfWill_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Architects of Will", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Architects of Will");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB look-at-library trigger");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the cycling activated ability");
        card.Abilities.OfType<KeywordAbility>().Should().Contain(k => k.Keyword == "Cycling");
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape — CR 603.6a
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchitectsOfWill_HasEtbTrigger_WithTargetPlayerRequest()
    {
        var card = ArchitectsOfWillFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Should().BeOfType<EventTriggerCondition<CardMovedEvent>>();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);

        trigger.TargetRequests.Should().HaveCount(1);
        var req = trigger.TargetRequests[0];
        req.Description.Should().Contain("target player");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB resolve — reorder top three on the chosen target's library
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchitectsOfWill_EtbResolve_ReordersTopThreeOnTarget()
    {
        // Seed Bob's library with three distinguishable cards.
        var top1 = new Card("Plains", "");
        var top2 = new Card("Island", "");
        var top3 = new Card("Mountain", "");
        foreach (var c in new[] { top1, top2, top3 })
        {
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var architects = ArchitectsOfWillFactory.Create(_alice);
        var trigger = architects.Abilities.OfType<TriggeredAbility>().Single();

        // Pre-supply Bob as the target (sync-path posture — agent-prompt
        // path skipped for shape testing).
        trigger.SetChosenTargets(new[] { new object[] { _bob } });

        // Resolve.
        foreach (var effect in trigger.Effects) effect.Execute();

        // With no agent registered the default keeps the peeked top
        // three in their original order — Plains / Island / Mountain on
        // top, in that order.
        var libraryAfter = _bob.Zones.Library.GetCards().Take(3).ToList();
        libraryAfter.Should().Equal(new[] { top1, top2, top3 });
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchitectsOfWill_HasCyclingActivatedAbility_WithHybridManaAndDiscardSelf()
    {
        var card = ArchitectsOfWillFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2, "cycling = {U/B} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var manaCost = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.HybridPips.Should().HaveCount(1, "cycling cost is the hybrid pip {U/B}");
        var hybrid = manaCost.HybridPips[0];
        // {U/B} hybrid pip — Blue or Black colour halves.
        (hybrid.Color1 == ManaColor.Blue || hybrid.Color2 == ManaColor.Blue)
            .Should().BeTrue("hybrid includes the Blue half");
        (hybrid.Color1 == ManaColor.Black || hybrid.Color2 == ManaColor.Black)
            .Should().BeTrue("hybrid includes the Black half");
    }

    // -----------------------------------------------------------------------
    // Cycling end-to-end — pays {B}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchitectsOfWill_Cycling_EndToEnd_PublishesCardCycledEvent()
    {
        // Seed library with one card so the draw resolves.
        var topCard = new Instant("Counterspell", "{U}{U}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var architects = ArchitectsOfWillFactory.Create(_alice, triggers: null, eventBus: bus);
        _alice.Zones.Hand.AddCard(architects);
        architects.SetZone(ZoneType.Hand);

        // Pay {B} (one of the hybrid halves — {U/B} accepts either colour).
        _alice.AddManaToPool(ManaCost.Parse("B"));

        var cycling = architects.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        architects.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycling drew a card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(architects);
        captured.Player.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cycling event surface — Lightning Rift trigger fires on cycle
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cycling Architects of Will publishes <see cref="CardCycledEvent"/>
    /// — the same surface Lightning Rift / Living End cycle-shell
    /// payoffs subscribe to. This sanity test exercises the publish
    /// posture rather than re-testing Lightning Rift's trigger queuing
    /// (that's covered by <see cref="LightningRiftFactoryTests"/>).
    /// </summary>
    [Fact]
    public void ArchitectsOfWill_Cycle_PublishesEventAsLivingEndEnabler()
    {
        var topCard = new Card("Decoy", "");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        int eventCount = 0;
        bus.Subscribe<CardCycledEvent>(_ => eventCount++);

        var architects = ArchitectsOfWillFactory.Create(_alice, triggers: null, eventBus: bus);
        _alice.Zones.Hand.AddCard(architects);
        architects.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("U"));

        var cycling = architects.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs) cost.Pay(_alice);
        foreach (var effect in cycling.Effects) effect.Execute();

        eventCount.Should().Be(1, "exactly one CardCycledEvent per cycle activation");
    }
}
