using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DeadshotMinotaurFactory"/> (Khans of Tarkir).
///
/// Deadshot Minotaur ({3}{R}{G}). Creature — Minotaur 3/4. Oracle:
///   "When this creature enters, it deals 3 damage to target creature with
///    flying.
///    Cycling {R/G} ({R/G}, Discard this card: Draw a card.)"
///
/// Coverage:
/// - Identity (name, type, subtype, cost, P/T, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB damage trigger shape (CardMovedEvent → battlefield) + a
///   "target creature with flying" TargetRequest whose gatherer narrows to
///   fliers only (CR 702.9).
/// - ETB resolution deals 3 damage to a flying target; CR 608.2b no-op when
///   the target lacks Flying.
/// - Cycling activated ability shape ({R/G} hybrid mana + DiscardSelfCost).
/// - Cycling end-to-end: pays {R/G}, discards self, draws, publishes
///   CardCycledEvent.
/// </summary>
[Trait("Color", "M")]
public class DeadshotMinotaurFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeFlier(Player owner, string name = "Wind Drake")
    {
        var c = new Creature(name, "{2}{U}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.AddAbility(new KeywordAbility("Flying", c, owner));
        return c;
    }

    private static Creature MakeGroundCreature(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DeadshotMinotaur_Identity_Minotaur34()
    {
        var card = DeadshotMinotaurFactory.Create(_alice);

        card.Name.Should().Be("Deadshot Minotaur");
        card.ManaCost.ToString().Should().Be("{3}{R}{G}");
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(4);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Minotaur).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // ETB trigger — CR 603.6a, restricted target (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void DeadshotMinotaur_EtbTrigger_RequestsTargetCreatureWithFlying()
    {
        var card = DeadshotMinotaurFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().ContainSingle();
        var request = trigger.TargetRequests[0];
        request.Description.Should().Be("target creature with flying");
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void DeadshotMinotaur_EtbGatherer_NarrowsToFliersOnly()
    {
        var card = DeadshotMinotaurFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var flier = MakeFlier(_bob);
        var ground = MakeGroundCreature(_bob);
        var ctx = new Majik.Core.Game.GameContext(
            _alice, new[] { _alice, _bob }, _alice, 1,
            Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack());

        var candidates = trigger.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(flier);
        candidates.Should().NotContain(ground,
            "only creatures with flying are legal targets (CR 702.9)");
    }

    [Fact]
    public void DeadshotMinotaur_EtbResolution_Deals3ToFlyingTarget()
    {
        var card = DeadshotMinotaurFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var flier = MakeFlier(_bob); // 2/2 with Flying
        trigger.SetChosenTargets(new[] { new object[] { flier } });

        foreach (var effect in trigger.Effects) effect.Execute();

        flier.Damage.Should().Be(3,
            "the ETB deals 3 damage to the flying target (CR 603.6a)");
    }

    [Fact]
    public void DeadshotMinotaur_EtbResolution_NoOpWhenTargetLacksFlying()
    {
        var card = DeadshotMinotaurFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        // Resolution-time the chosen target no longer has Flying (CR 608.2b).
        var ground = MakeGroundCreature(_bob);
        trigger.SetChosenTargets(new[] { new object[] { ground } });

        foreach (var effect in trigger.Effects) effect.Execute();

        ground.Damage.Should().Be(0,
            "CR 608.2b — an illegal (non-flying) target at resolution is a no-op");
    }

    // -----------------------------------------------------------------------
    // Cycling ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void DeadshotMinotaur_HasCyclingActivatedAbility_WithHybridAndDiscardSelf()
    {
        var card = DeadshotMinotaurFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.HybridPips.Should().ContainSingle("cycling {R/G} is one hybrid pip");
    }

    // -----------------------------------------------------------------------
    // Cycling end-to-end — pays {R/G}, discards, draws, publishes event
    // -----------------------------------------------------------------------

    [Fact]
    public void DeadshotMinotaur_Cycling_EndToEnd_DiscardsDrawsPublishesEvent()
    {
        var topCard = new Instant("Lightning Bolt", "{R}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var minotaur = DeadshotMinotaurFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(minotaur);
        minotaur.SetZone(ZoneType.Hand);
        _alice.AddManaToPool(ManaCost.Parse("R"));

        var cycling = minotaur.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }

        minotaur.Zone.Should().Be(ZoneType.Graveyard);

        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "cycling drew a card");
        captured.Should().NotBeNull("CR 702.32d publication");
        captured!.Card.Should().BeSameAs(minotaur);
        captured.Player.Should().BeSameAs(_alice);
    }
}
