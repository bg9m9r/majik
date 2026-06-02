using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Vile Aggregate — Creature — Eldrazi Drone {2}{R},
/// printed P/T */5:
///   "Devoid (This card has no color.)
///    Vile Aggregate's power is equal to the number of colorless creatures
///    you control.
///    Trample
///    Ingest (Whenever this creature deals combat damage to a player, that
///    player exiles the top card of their library.)"
///
/// Covers:
///   * Card identity + Devoid colorlessness (CR 702.114).
///   * Layer-7a power CDA = colorless-creature count, printed toughness 5
///     preserved (CR 604.3 / 613.2).
///   * Trample keyword marker (CR 702.19).
///   * Ingest combat-damage trigger exiling the top of the damaged player's
///     library (CR 702.115 / CR 510).
/// </summary>
public class VileAggregateTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;
    private readonly TriggerManager _triggers;

    public VileAggregateTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
        _triggers = new TriggerManager(_stack, _bus);
    }

    private Func<IEnumerable<ICard>> AliceCreatures => () => _alice.Zones.Battlefield.GetCards();

    private Creature WireVileAggregate(Player owner)
    {
        var card = VileAggregateFactory.Create(
            owner, _effects, _bus, _triggers, _zones, AliceCreatures);
        card.ActiveEffects = _effects;
        // Seed into the library so the MoveCard(Library -> Battlefield) in each
        // test relocates a card the zone manager already tracks (so it lands in
        // owner.Zones.Battlefield, not just card.Zone == Battlefield).
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    // -----------------------------------------------------------------------
    // Card identity + Devoid
    // -----------------------------------------------------------------------

    [Fact]
    public void VileAggregate_IsEldraziDrone_AtCost2R()
    {
        var card = VileAggregateFactory.Create(_alice);

        card.Name.Should().Be("Vile Aggregate");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.BaseToughness.Should().Be(5);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VileAggregate_IsColorless_ViaDevoid()
    {
        var card = VileAggregateFactory.Create(_alice);

        // CR 702.114 — Devoid: colorless despite the {R} pip.
        CardColors.GetColors(card).Should().BeEmpty();
        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Devoid");
    }

    [Fact]
    public void VileAggregate_HasTrample()
    {
        var card = VileAggregateFactory.Create(_alice);

        // CR 702.19 — Trample keyword marker read by CombatAbilities.HasTrample.
        CombatAbilities.HasTrample(card).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VileAggregate()
    {
        var card = NamedCardFactory.Create("Vile Aggregate", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Vile Aggregate");
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Layer 7a — power CDA tracks colorless-creature count, toughness stays 5
    // -----------------------------------------------------------------------

    [Fact]
    public void VileAggregate_AloneOnBattlefield_PowerIsOne_ToughnessFive()
    {
        // Only Vile Aggregate (itself colorless) on Alice's battlefield → 1/5.
        var card = WireVileAggregate(_alice);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        card.Power.Should().Be(1);
        card.Toughness.Should().Be(5);
    }

    [Fact]
    public void VileAggregate_PowerCountsColorlessCreatures_IgnoresColoredAndNonCreatures()
    {
        var card = WireVileAggregate(_alice);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Two more colorless creatures (no colored pips) on Alice's side.
        var spawn1 = new Card("Eldrazi Spawn", "", new[] { CardType.Creature });
        var spawn2 = new Card("Eldrazi Scion", "", new[] { CardType.Creature });
        // A colored creature — must NOT count (CR 105.2c).
        var bear = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        // A colorless non-creature — must NOT count.
        var solRing = new Card("Sol Ring", "1", new[] { CardType.Artifact });

        foreach (var c in new[] { spawn1, spawn2, bear, solRing })
        {
            c.SetOwner(_alice);
            c.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(c);
            c.SetZone(ZoneType.Battlefield);
        }

        // Vile Aggregate + 2 colorless spawns = 3 colorless creatures.
        card.Power.Should().Be(3);
        card.Toughness.Should().Be(5);
    }

    [Fact]
    public void VileAggregate_PowerCountsOnlyYourCreatures_NotOpponents()
    {
        var card = WireVileAggregate(_alice);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Colorless creature controlled by Bob — must NOT count toward
        // Alice's Vile Aggregate ("creatures YOU control").
        var bobsSpawn = new Card("Eldrazi Spawn", "", new[] { CardType.Creature });
        bobsSpawn.SetOwner(_bob);
        bobsSpawn.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobsSpawn);
        bobsSpawn.SetZone(ZoneType.Battlefield);

        // Only Vile Aggregate itself counts.
        card.Power.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Ingest — combat damage to a player exiles top of their library
    // -----------------------------------------------------------------------

    [Fact]
    public void VileAggregate_Ingest_ExilesTopOfDamagedPlayersLibrary()
    {
        var card = WireVileAggregate(_alice);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Seed Bob's library with a known top card.
        var top = new Card("Forest", "", new[] { CardType.Land });
        var beneath = new Card("Mountain", "", new[] { CardType.Land });
        top.SetOwner(_bob);
        beneath.SetOwner(_bob);
        _bob.Zones.Library.AddCard(top);
        _bob.Zones.Library.AddCard(beneath);

        // Vile Aggregate deals combat damage to Bob → Ingest fires.
        _bus.Publish(new CombatDamageDealtEvent(card, _bob, amount: 1));
        _triggers.PutPendingTriggersOnStack(_alice);
        _stack.Pop()!.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(top);
        _bob.Zones.Library.GetCards().Should().NotContain(top);
        _bob.Zones.Library.GetCards().Should().Contain(beneath);
    }

    [Fact]
    public void VileAggregate_Ingest_EmptyLibrary_IsNoOp()
    {
        var card = WireVileAggregate(_alice);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Bob has an empty library — Ingest must not throw; SBAs own the loss.
        _bus.Publish(new CombatDamageDealtEvent(card, _bob, amount: 1));
        _triggers.PutPendingTriggersOnStack(_alice);
        var act = () => _stack.Pop()!.Resolve();
        act.Should().NotThrow();

        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void VileAggregate_Ingest_DoesNotFire_OnCombatDamageToCreature()
    {
        var card = WireVileAggregate(_alice);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        var top = new Card("Forest", "", new[] { CardType.Land });
        top.SetOwner(_bob);
        _bob.Zones.Library.AddCard(top);

        var blocker = new Creature("Wall", "0", 0, 4) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(blocker);
        blocker.SetZone(ZoneType.Battlefield);

        // Combat damage to a creature (not a player) — Ingest must not fire.
        _bus.Publish(new CombatDamageDealtEvent(card, (ICard)blocker, amount: 1));
        _triggers.PendingCount.Should().Be(0,
            "Ingest only triggers on combat damage to a player (CR 702.115)");

        _bob.Zones.Library.GetCards().Should().Contain(top);
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }
}
