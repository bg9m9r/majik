using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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
/// Unit tests for <see cref="NarnamRenegadeFactory"/>.
///
/// Card: Narnam Renegade — Creature — Elf Warrior {G} 1/2 (Aether Revolt).
///   "Deathtouch
///    Revolt — This creature enters with a +1/+1 counter on it if a permanent
///    left the battlefield under your control this turn."
///
/// Covers:
///   - Identity / dispatch / Deathtouch keyword marker.
///   - Revolt INACTIVE → enters vanilla 1/2 (no counter).
///   - Revolt ACTIVE (a permanent the controller controlled left the
///     battlefield this turn) → enters with one +1/+1 counter (a 2/3).
///   - Null TurnState resolver (shape path) → revolt inactive → no counter.
///   - Single-arg create (no bus / no resolver) is shape-only.
/// </summary>
public class NarnamRenegadeTests
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
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NarnamRenegade_Identity()
    {
        var c = NarnamRenegadeFactory.Create(_alice);

        c.Name.Should().Be("Narnam Renegade");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NarnamRenegade_HasDeathtouchMarker()
    {
        var c = NarnamRenegadeFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Deathtouch", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Narnam Renegade has Deathtouch (CR 702.2)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_NarnamRenegade()
    {
        var card = NamedCardFactory.Create("Narnam Renegade", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Narnam Renegade");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{G}");
    }

    // -----------------------------------------------------------------------
    // Revolt-gated enters-with-counter (CR 702.104a / CR 614.1d)
    // -----------------------------------------------------------------------

    [Fact]
    public void RevoltInactive_EntersVanilla()
    {
        var bus = new ReplacementBus();
        var turnState = new TurnState();

        var card = NarnamRenegadeFactory.Create(_alice, bus, () => turnState);
        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no permanent left the battlefield under the controller this turn → vanilla 1/2");
    }

    [Fact]
    public void RevoltActive_EntersWithOneCounter()
    {
        var bus = new ReplacementBus();
        var turnState = new TurnState();

        var card = NarnamRenegadeFactory.Create(_alice, bus, () => turnState);

        // A permanent the controller controlled left the battlefield this turn.
        turnState.RecordPermanentLeftBattlefield(_alice);
        turnState.RevoltActive(_alice).Should().BeTrue();

        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Revolt active → enters with one +1/+1 counter (a 2/3)");
    }

    [Fact]
    public void NullTurnStateResolver_EntersVanilla()
    {
        var bus = new ReplacementBus();

        var card = NarnamRenegadeFactory.Create(_alice, bus, () => null);
        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no TurnState wired → revolt inactive → vanilla 1/2");
    }

    [Fact]
    public void SingleArgFactory_NoCounterReplacement()
    {
        var bus = new ReplacementBus();

        // Single-arg shape path: no revolt resolver registered, so even with a
        // bus the card enters vanilla.
        var card = NarnamRenegadeFactory.Create(_alice);
        EnterBattlefield(card, _alice, bus);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "single-arg create wires no enters-with-counter replacement");
    }
}
