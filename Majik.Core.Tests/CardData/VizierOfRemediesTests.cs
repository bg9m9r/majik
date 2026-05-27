using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="VizierOfRemediesFactory"/> (Amonkhet, {W}{W}).
///
/// Card: Vizier of Remedies — Creature — Human Cleric 2/1.
///   "If a -1/-1 counter would be put on a creature you control,
///    prevent that. Instead put no counter on that creature."
///
/// Covers:
///   - Identity / dispatch.
///   - -1/-1 counter placement on your creature replaced to zero.
///   - +1/+1 placement NOT affected.
///   - Opponent's creature NOT affected.
///   - Inert while not on battlefield.
/// </summary>
public class VizierOfRemediesTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void VizierOfRemedies_Identity()
    {
        var c = VizierOfRemediesFactory.Create(_alice);

        c.Name.Should().Be("Vizier of Remedies");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Subtypes.Should().Contain(CardSubtype.Human);
        c.Subtypes.Should().Contain(CardSubtype.Cleric);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VizierOfRemedies()
    {
        var card = NamedCardFactory.Create("Vizier of Remedies", _alice);
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Vizier of Remedies");
    }

    [Fact]
    public void Replaces_MinusOneCounter_OnControlledCreature_WithZero()
    {
        var bus = new ReplacementBus();
        var vizier = VizierOfRemediesFactory.Create(_alice, bus);
        PlaceOnBattlefield(vizier, _alice);

        var bear = MakeCreature("Bear", _alice);
        PlaceOnBattlefield(bear, _alice);

        var placed = CountersService.Add(bear, CounterType.MinusOneMinusOne, 1, bus);

        placed.Should().Be(0, "Vizier replaces the -1/-1 placement with zero counters");
        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(0);
    }

    [Fact]
    public void Does_Not_Affect_PlusOneCounter()
    {
        var bus = new ReplacementBus();
        var vizier = VizierOfRemediesFactory.Create(_alice, bus);
        PlaceOnBattlefield(vizier, _alice);

        var bear = MakeCreature("Bear", _alice);
        PlaceOnBattlefield(bear, _alice);

        var placed = CountersService.Add(bear, CounterType.PlusOnePlusOne, 1, bus);

        placed.Should().Be(1, "Vizier only scopes to -1/-1 counters");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Does_Not_Affect_OpponentCreature()
    {
        var bus = new ReplacementBus();
        var vizier = VizierOfRemediesFactory.Create(_alice, bus);
        PlaceOnBattlefield(vizier, _alice);

        var bobBear = MakeCreature("Bear", _bob);
        PlaceOnBattlefield(bobBear, _bob);

        var placed = CountersService.Add(bobBear, CounterType.MinusOneMinusOne, 2, bus);

        placed.Should().Be(2, "Vizier is one-sided ('a creature you control')");
        bobBear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(2);
    }

    [Fact]
    public void Inert_OffBattlefield()
    {
        var bus = new ReplacementBus();
        var vizier = VizierOfRemediesFactory.Create(_alice, bus);
        // Don't place on battlefield.

        var bear = MakeCreature("Bear", _alice);
        PlaceOnBattlefield(bear, _alice);

        var placed = CountersService.Add(bear, CounterType.MinusOneMinusOne, 1, bus);

        placed.Should().Be(1, "Vizier must be on the battlefield for its replacement to fire");
        bear.Counters.Count(CounterType.MinusOneMinusOne).Should().Be(1);
    }

    [Fact]
    public void SingleArgFactory_DoesNotRegisterReplacement()
    {
        var vizier = VizierOfRemediesFactory.Create(_alice);
        vizier.Should().NotBeNull();
        vizier.Name.Should().Be("Vizier of Remedies");
    }

    private static Creature MakeCreature(string name, Player owner)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private static void PlaceOnBattlefield(Permanent p, Player owner)
    {
        owner.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
    }
}
