using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Spirit of the Labyrinth — Enchantment Creature — Spirit
/// {1}{W}, 3/1 (Born of the Gods). Oracle text (verified against
/// Scryfall): "Each player can't draw more than one card each turn."
///
/// This is the SYMMETRIC sibling of Narset, Parter of Veils' printed
/// static (CR 117.1a): where Narset restricts only opponents, Spirit's
/// "each player" caps every player (its own controller included) at one
/// draw per turn. It reuses the same engine primitive — a
/// <see cref="DrawCardIntent"/> replacement (CR 614) registered on each
/// affected player's <see cref="ReplacementBus"/>, reset on
/// <see cref="TurnStartedEvent"/>.
///
/// Covers:
/// - Identity / type / subtype / P-T / dispatcher routing.
/// - Symmetric draw cap: BOTH players capped at one draw per turn.
/// - Per-turn reset on <see cref="TurnStartedEvent"/>.
/// - LTB releases the restriction for every affected player.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "W")]
public class SpiritOfTheLabyrinthTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public SpiritOfTheLabyrinthTests()
    {
        _zones = new ZoneService(_bus);
        _alice.AttachReplacementBus(new ReplacementBus());
        _bob.AttachReplacementBus(new ReplacementBus());
    }

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Spirit_HasCorrectIdentity_TypesSubtype_AndPowerToughness()
    {
        var spirit = SpiritOfTheLabyrinthFactory.Create(_alice);

        spirit.Name.Should().Be("Spirit of the Labyrinth");
        spirit.ManaCost.Should().Be("{1}{W}");
        spirit.HasType(CardType.Creature).Should().BeTrue();
        spirit.HasType(CardType.Enchantment).Should().BeTrue();
        spirit.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        spirit.Power.Should().Be(3);
        spirit.Toughness.Should().Be(1);
        spirit.Owner.Should().BeSameAs(_alice);
        spirit.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesSpiritOfTheLabyrinth_ToFactory()
    {
        var card = NamedCardFactory.Create("Spirit of the Labyrinth", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spirit of the Labyrinth");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Printed static — CR 117.1a "Each player can't draw more than 1/turn"
    // -----------------------------------------------------------------------

    [Fact]
    public void SpiritOnBattlefield_CapsOpponentAtOneDrawPerTurn()
    {
        SeedLibrary(_bob, 3);

        var spirit = SpiritOfTheLabyrinthFactory.Create(
            _alice,
            playerResolver: () => new[] { _alice, _bob },
            eventBus: _bus);

        _alice.Zones.Library.AddCard(spirit);
        spirit.SetZone(ZoneType.Library);
        _zones.MoveCard(spirit, ZoneType.Library, ZoneType.Battlefield);

        var drawn = Fx.DrawCards(_bob, 3);

        drawn.Should().HaveCount(1);
        _bob.Zones.Hand.GetCards().Should().HaveCount(1);
        _bob.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void SpiritOnBattlefield_AlsoCapsItsOwnController_Symmetric()
    {
        // Unlike Narset (opponents only), Spirit's "each player" includes
        // the controller. Alice must be capped too.
        SeedLibrary(_alice, 3);

        var spirit = SpiritOfTheLabyrinthFactory.Create(
            _alice,
            playerResolver: () => new[] { _alice, _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(spirit);
        spirit.SetZone(ZoneType.Library);
        _zones.MoveCard(spirit, ZoneType.Library, ZoneType.Battlefield);

        var drawn = Fx.DrawCards(_alice, 3);

        drawn.Should().HaveCount(1);
        _alice.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void SpiritRestriction_ResetsOnTurnStart_AllowingOneMoreDrawNextTurn()
    {
        SeedLibrary(_bob, 4);

        var spirit = SpiritOfTheLabyrinthFactory.Create(
            _alice,
            playerResolver: () => new[] { _alice, _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(spirit);
        spirit.SetZone(ZoneType.Library);
        _zones.MoveCard(spirit, ZoneType.Library, ZoneType.Battlefield);

        Fx.DrawCards(_bob, 2);
        _bob.Zones.Hand.GetCards().Should().HaveCount(1);

        _bus.Publish(new TurnStartedEvent(_bob, 2));

        Fx.DrawCards(_bob, 2);
        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void SpiritLeavingBattlefield_ReleasesRestriction_ForAllPlayers()
    {
        SeedLibrary(_bob, 3);

        var spirit = SpiritOfTheLabyrinthFactory.Create(
            _alice,
            playerResolver: () => new[] { _alice, _bob },
            eventBus: _bus);
        _alice.Zones.Library.AddCard(spirit);
        spirit.SetZone(ZoneType.Library);
        _zones.MoveCard(spirit, ZoneType.Library, ZoneType.Battlefield);

        _zones.MoveCard(spirit, ZoneType.Battlefield, ZoneType.Graveyard);

        var drawn = Fx.DrawCards(_bob, 3);
        drawn.Should().HaveCount(3);
        _bob.Zones.Hand.GetCards().Should().HaveCount(3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Creature($"Stub-{i}", "", 1, 1);
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
