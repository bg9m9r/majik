using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TheMeathookMassacreFactory"/> (Innistrad:
/// Midnight Hunt, {X}{B}{B}).
///
/// Covers:
/// - Identity (name, type Enchantment, Legendary supertype, mana cost,
///   owner / controller).
/// - NamedCardFactory dispatch.
/// - ETB sweep at X=2: every creature on every player's battlefield gets
///   -2/-2 via a per-creature PumpUntilEndOfTurnEffect on its
///   ActiveEffects service.
/// - ETB sweep at X=0: no-op (PendingCastX was never stamped).
/// - Opponent-creature dies → controller gains 1 life (CR 603.1 +
///   CR 700.4).
/// - Own-creature dies → each opponent loses 1 life (with resolver).
/// - Own-creature dies without resolver silently no-ops.
/// - Dies-trigger predicates ignore non-creature graveyard moves and
///   moves to other zones.
/// </summary>
public class TheMeathookMassacreTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Meathook_Identity()
    {
        var c = TheMeathookMassacreFactory.Create(_alice);

        c.Name.Should().Be("The Meathook Massacre");
        c.ManaCost.Should().Be("{X}{B}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "CR 205.4 — The Meathook Massacre is Legendary");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // Three triggered abilities — ETB sweep + opp-dies drain +
        // own-dies drain.
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(3,
            "Massacre has ETB -X/-X + opp-dies +1 life + own-dies -1 life");
    }

    [Fact]
    public void Meathook_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("The Meathook Massacre", _alice);

        c.Should().BeOfType<Enchantment>();
        c.Name.Should().Be("The Meathook Massacre");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB sweep (CR 603.6a / CR 122.1g)
    // -----------------------------------------------------------------------

    [Fact]
    public void Meathook_EtbAtXEqualsTwo_AllCreaturesGetMinusTwoMinusTwo()
    {
        // Wire each creature with its own ActiveEffects service — mirrors
        // the way SpellCastFlow / the live engine hooks creatures up so
        // PumpUntilEndOfTurnEffect can be registered. Two creatures per
        // player exercise the all-players sweep.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        giant.SetOwner(_bob);
        giant.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(giant);
        giant.SetZone(ZoneType.Battlefield);

        var massacre = TheMeathookMassacreFactory.Create(
            _alice,
            opponentResolver: null,
            allPlayersResolver: () => new[] { _alice, _bob },
            eventBus: null,
            triggers: null);

        // Simulate SpellCastFlow stamping X=2 at cast time.
        massacre.SetPendingCastX(2);

        // Place Massacre on the battlefield and resolve the ETB trigger.
        _alice.Zones.Battlefield.AddCard(massacre);
        massacre.SetZone(ZoneType.Battlefield);

        var triggers = massacre.Abilities.OfType<TriggeredAbility>().ToList();
        var etbTrigger = triggers.Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>
            && t.IsTriggered(new CardMovedEvent(massacre, ZoneType.Stack, ZoneType.Battlefield)));

        foreach (var e in etbTrigger.Effects) e.Execute();

        // Both creatures see -2/-2 — CR 613 Layer 7c.
        bear.Power.Should().Be(0, "Grizzly Bears starts 2/2, gets -2/-2 ⇒ 0/0");
        bear.Toughness.Should().Be(0);
        giant.Power.Should().Be(1, "Hill Giant starts 3/3, gets -2/-2 ⇒ 1/1");
        giant.Toughness.Should().Be(1);

        // PendingCastX consumed so a re-entry won't re-sweep.
        massacre.PendingCastX.Should().BeNull(
            "the ETB effect clears PendingCastX so blink/copy can't reuse it");
    }

    [Fact]
    public void Meathook_EtbWithoutPendingX_IsNoOp()
    {
        // No SetPendingCastX call — non-cast entries (e.g. blink, token
        // copy) leave PendingCastX null and should not touch creature
        // stats. Mirrors Chalice of the Void's "no charge counters when
        // not cast" path.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var massacre = TheMeathookMassacreFactory.Create(
            _alice,
            opponentResolver: null,
            allPlayersResolver: () => new[] { _alice },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(massacre);
        massacre.SetZone(ZoneType.Battlefield);

        var etbTrigger = massacre.Abilities.OfType<TriggeredAbility>()
            .First(t => t.IsTriggered(new CardMovedEvent(massacre, ZoneType.Stack, ZoneType.Battlefield)));

        foreach (var e in etbTrigger.Effects) e.Execute();

        bear.Power.Should().Be(2, "no PendingCastX ⇒ no sweep");
        bear.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Dies triggers (CR 603.1 / CR 700.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Meathook_OpponentCreatureDies_ControllerGainsOneLife()
    {
        var massacre = TheMeathookMassacreFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            allPlayersResolver: null,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(massacre);
        massacre.SetZone(ZoneType.Battlefield);

        // Bob's creature dies — its controller is Bob (not Alice).
        var bobsBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobsBear.SetOwner(_bob);
        bobsBear.SetController(_bob);

        var diesEvent = new CardMovedEvent(bobsBear, ZoneType.Battlefield, ZoneType.Graveyard);

        // The opp-dies trigger is the one whose condition matches a
        // Bob-controlled creature dying.
        var oppDiesTrigger = massacre.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in oppDiesTrigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "controller gains 1 life on each opp-creature dies");
        _bob.LifeTotal.Should().Be(20, "Bob is unaffected by this trigger");
    }

    [Fact]
    public void Meathook_OwnCreatureDies_EachOpponentLosesOneLife()
    {
        var massacre = TheMeathookMassacreFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            allPlayersResolver: null,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(massacre);
        massacre.SetZone(ZoneType.Battlefield);

        // Alice's own creature dies.
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var ownDiesTrigger = massacre.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in ownDiesTrigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(20, "controller is not drained by their own creature dying");
        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life when an own-creature dies");
    }

    [Fact]
    public void Meathook_OwnCreatureDies_WithoutResolver_IsNoOp()
    {
        // Single-arg dispatcher path — no opponentResolver wired. The
        // own-dies trigger still fires for shape but the drain silently
        // no-ops, mirroring Sheoldred's resolver convention.
        var massacre = TheMeathookMassacreFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(massacre);
        massacre.SetZone(ZoneType.Battlefield);

        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        aliceBear.SetController(_alice);

        var diesEvent = new CardMovedEvent(aliceBear, ZoneType.Battlefield, ZoneType.Graveyard);

        var ownDiesTrigger = massacre.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(diesEvent));

        foreach (var e in ownDiesTrigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20, "no resolver ⇒ no drain");
    }

    [Fact]
    public void Meathook_NonCreatureDies_DoesNotFireDiesTriggers()
    {
        // CR 700.4 — dying applies only to creatures. An artifact or
        // enchantment moving Battlefield → Graveyard must not satisfy
        // either dies-trigger predicate.
        var massacre = TheMeathookMassacreFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            allPlayersResolver: null,
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(massacre);
        massacre.SetZone(ZoneType.Battlefield);

        // A Bob-controlled artifact moving Battlefield → Graveyard.
        var trinket = new Artifact("Trinket", "{0}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);

        var moveEvent = new CardMovedEvent(trinket, ZoneType.Battlefield, ZoneType.Graveyard);

        var diesTriggers = massacre.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(moveEvent))
            .ToList();

        diesTriggers.Should().BeEmpty(
            "neither dies-trigger fires for a non-creature moving to graveyard — CR 700.4");
    }
}
