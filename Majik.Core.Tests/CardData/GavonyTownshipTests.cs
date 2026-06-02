using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GavonyTownshipFactory"/>.
///
/// Card: Gavony Township — Land (Innistrad).
///   "{T}: Add {C}.
///    {2}{G}{W}, {T}: Put a +1/+1 counter on each creature you control."
///
/// Covers:
///   - Identity (Land, no printed mana cost) + NamedCardFactory dispatch.
///   - {T}: Add {C} mana ability from the embedded JSON.
///   - Activated ability costs: {2}{G}{W} (mana) + {T} (tap).
///   - Activated resolve puts a +1/+1 counter on every controlled creature.
///   - The land itself is never pumped (it isn't a creature).
///   - Opponent's creatures aren't touched ("you control").
///   - Hardened-Scales-shaped replacement bus bumps each placement.
/// </summary>
public class GavonyTownshipTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void GavonyTownship_Identity()
    {
        var land = GavonyTownshipFactory.Create(_alice);

        land.Name.Should().Be("Gavony Township");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);

        // {T}: Add {C} mana ability from the embedded JSON.
        land.Abilities.OfType<ManaAbility>().Should().ContainSingle(
            "the {T}: Add {C} mana ability is declared in gavony-township.json");
    }

    [Fact]
    public void GavonyTownship_DispatchesViaNamedCardFactory()
    {
        var land = NamedCardFactory.Create("Gavony Township", _alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Gavony Township");
        land.Abilities.OfType<ManaAbility>().Should().ContainSingle();
        land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the {2}{G}{W}, {T}: +1/+1 counter activated ability is attached");
    }

    [Fact]
    public void ActivatedAbility_HasManaAndTapCosts()
    {
        var land = GavonyTownshipFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.Should().Contain(c => c is ManaCostCost,
            "the {2}{G}{W} mana pips are a cost on the activated ability");
        activated.Costs.Should().Contain(c => c is AdditionalCost,
            "the {T} tap symbol is a cost on the activated ability");
    }

    [Fact]
    public void Activated_Resolve_PumpsEachControlledCreature()
    {
        var land = GavonyTownshipFactory.Create(_alice);
        PutOnBattlefield(_alice, land);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, bear);

        var elf = new Creature("Llanowar Elves", "{G}", 1, 1);
        PutOnBattlefield(_alice, elf);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        elf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        elf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Activated_Resolve_DoesNotPumpTheLandItself()
    {
        var land = GavonyTownshipFactory.Create(_alice);
        PutOnBattlefield(_alice, land);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Gavony Township is a land, not a creature — it never receives a counter");
    }

    [Fact]
    public void Activated_Resolve_DoesNotTouchOpponentsCreatures()
    {
        var land = GavonyTownshipFactory.Create(_alice);
        PutOnBattlefield(_alice, land);

        var ownCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, ownCreature);

        var opponentCreature = new Creature("Llanowar Elves", "{G}", 1, 1);
        PutOnBattlefield(_bob, opponentCreature);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        ownCreature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        opponentCreature.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the opponent's creature isn't touched — printed text says 'you control'");
    }

    [Fact]
    public void Activated_Resolve_RoutesThroughReplacementBus_HonoursHardenedScalesShape()
    {
        var bus = new ReplacementBus();
        var land = GavonyTownshipFactory.Create(_alice, bus);
        PutOnBattlefield(_alice, land);

        var hardenedScales = HardenedScalesFactory.Create(_alice, bus);
        PutOnBattlefield(_alice, hardenedScales);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, bear);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in activated.Effects) e.Execute();

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "Hardened Scales bumps +1 -> +2 on each creature you control");
    }
}
