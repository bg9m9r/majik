using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CacklingSlasherFactory"/>.
///
/// Card: Cackling Slasher — Creature — Human Assassin {3}{B} 3/3 (Duskmourn).
///   "Deathtouch
///    This creature enters with a +1/+1 counter on it if a creature died this turn."
///
/// Covers (card-unique behaviour only — CardFactoryContractTests already
/// asserts dispatch + well-formedness for every implemented card):
///   - Identity (mana cost / P-T / subtypes).
///   - Deathtouch keyword marker (CR 702.2).
///   - No creature died → enters vanilla 3/3 (no counter).
///   - A creature died this turn (CR 700.4, ANY creature) → enters with one
///     +1/+1 counter (a 4/4).
///   - Null TurnState resolver (shape path) → gate false → no counter.
///   - Single-arg create (no bus / no resolver) is shape-only.
/// </summary>
[Trait("Color", "B")]
public class CacklingSlasherTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void EnterBattlefield(Creature card, Player owner, ReplacementBus bus)
    {
        owner.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, owner);
    }

    // -----------------------------------------------------------------------
    // Identity + Deathtouch
    // -----------------------------------------------------------------------

    [Fact]
    public void CacklingSlasher_Identity()
    {
        var c = CacklingSlasherFactory.Create(_alice);

        c.Name.Should().Be("Cackling Slasher");
        c.ManaCost.Should().Be("{3}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Assassin).Should().BeTrue();
        c.Power.Should().Be(3);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CacklingSlasher_HasDeathtouchMarker()
    {
        var c = CacklingSlasherFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Deathtouch", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Cackling Slasher has Deathtouch (CR 702.2)");
    }

    // -----------------------------------------------------------------------
    // Conditional enters-with-counter (CR 614.1d / CR 700.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void NoCreatureDied_EntersVanilla()
    {
        var bus = new ReplacementBus();
        var turnState = new TurnState();

        var card = CacklingSlasherFactory.Create(_alice, bus, () => turnState);
        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no creature died this turn → vanilla 3/3");
    }

    [Fact]
    public void CreatureDiedThisTurn_EntersWithOneCounter()
    {
        var bus = new ReplacementBus();
        var turnState = new TurnState();

        var card = CacklingSlasherFactory.Create(_alice, bus, () => turnState);

        // A creature died this turn — CR 700.4 counts ANY creature regardless of
        // controller, so record one under the opponent to prove the gate is
        // global, not controller-scoped (the key difference from Revolt).
        var bob = new Player("Bob", 20);
        turnState.RecordCreatureDied(bob);
        turnState.CreaturesDiedThisTurn.Should().Be(1);

        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a creature died this turn → enters with one +1/+1 counter (a 4/4)");
    }

    [Fact]
    public void NullTurnStateResolver_EntersVanilla()
    {
        var bus = new ReplacementBus();

        var card = CacklingSlasherFactory.Create(_alice, bus, () => null);
        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no TurnState wired → gate false → vanilla 3/3");
    }

    [Fact]
    public void SingleArgFactory_NoCounterReplacement()
    {
        var bus = new ReplacementBus();

        var card = CacklingSlasherFactory.Create(_alice);
        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "single-arg create wires no enters-with-counter replacement");
    }
}
