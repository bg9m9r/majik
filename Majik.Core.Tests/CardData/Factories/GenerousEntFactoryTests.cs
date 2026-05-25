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
/// Unit tests for <see cref="GenerousEntFactory"/> (The Lord of the
/// Rings: Tales of Middle-earth).
///
/// Covers:
/// - Identity ({5}{G} Creature — Treefolk 5/7).
/// - Reach + Forestcycling-marker + Cycling keyword markers.
/// - ETB trigger shape — single 1..1 "target player" request.
/// - ETB resolve gains the chosen target player 4 life.
/// - Cycling activated ability ({G} mana + DiscardSelfCost) shape.
/// - Cycling end-to-end pays {G}, discards, draws, publishes
///   <see cref="CardCycledEvent"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class GenerousEntFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousEnt_Identity_Treefolk57()
    {
        var card = GenerousEntFactory.Create(_alice);

        card.Name.Should().Be("Generous Ent");
        card.ManaCost.ToString().Should().Be("{5}{G}");
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(7);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Treefolk).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GenerousEnt_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Generous Ent", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Generous Ent");
        card.HasSubtype(CardSubtype.Treefolk).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB target-player-gains-4-life trigger");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the cycling activated ability");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Reach");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Cycling");
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Forestcycling",
                "Forestcycling marker surfaced even though typed-tutor body is deferred");
    }

    // -----------------------------------------------------------------------
    // ETB trigger shape — CR 603.6a
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousEnt_HasEtbTrigger_WithTargetPlayerRequest()
    {
        var card = GenerousEntFactory.Create(_alice);
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
    // ETB resolve — target player gains 4 life
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousEnt_EtbResolve_TargetPlayerGains4Life()
    {
        var card = GenerousEntFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // Pre-supply Bob as the target.
        trigger.SetChosenTargets(new[] { new object[] { _bob } });

        var bobLifeBefore = _bob.LifeTotal;
        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(bobLifeBefore + 4);
        _alice.LifeTotal.Should().Be(20, "controller unchanged when targeting opponent");
    }

    [Fact]
    public void GenerousEnt_EtbResolve_SelfTargetIsLegal()
    {
        var card = GenerousEntFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // Self-target — printed wording is "target player" (no opponent gate).
        trigger.SetChosenTargets(new[] { new object[] { _alice } });

        var aliceLifeBefore = _alice.LifeTotal;
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(aliceLifeBefore + 4);
    }

    [Fact]
    public void GenerousEnt_EtbResolve_NoTarget_IsNoOp()
    {
        var card = GenerousEntFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // No targets set — illegal/missing target per CR 608.2b.
        var aliceLifeBefore = _alice.LifeTotal;
        var bobLifeBefore = _bob.LifeTotal;

        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(aliceLifeBefore);
        _bob.LifeTotal.Should().Be(bobLifeBefore);
    }

    // -----------------------------------------------------------------------
    // Cycling activated ability — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerousEnt_HasCyclingActivatedAbility_WithGreenAndDiscardSelf()
    {
        var card = GenerousEntFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2, "cycling = {G} + DiscardSelfCost");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Green.Should().Be(1, "cycling {G} charges one green");
    }

    [Fact]
    public void GenerousEnt_Forestcycling_EndToEnd_TutorsForestAndPublishesCardCycledEvent()
    {
        // Seed library with a Forest + an Instant. Forestcycling should
        // tutor the Forest (CR 702.32d), not the Instant.
        var forest = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var noise = new Instant("Lightning Bolt", "{R}");
        noise.SetOwner(_alice);
        _alice.Zones.Library.AddCard(noise);
        noise.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var ent = GenerousEntFactory.Create(_alice, triggers: null, eventBus: bus);
        _alice.Zones.Hand.AddCard(ent);
        ent.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("G"));

        var cycling = ent.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue($"{cost.Description}");
            cost.Pay(_alice);
        }

        ent.Zone.Should().Be(ZoneType.Graveyard, "discarded self");

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(forest,
            "Forestcycling tutors a Forest card (CR 702.32d)");
        _alice.Zones.Hand.GetCards().Should().NotContain(noise,
            "Forestcycling filters to Forest subtype only");
        forest.Zone.Should().Be(ZoneType.Hand);

        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(ent);
        captured.Player.Should().BeSameAs(_alice);
    }
}
