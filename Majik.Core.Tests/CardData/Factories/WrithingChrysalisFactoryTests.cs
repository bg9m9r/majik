using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="WrithingChrysalisFactory"/> (Battle for Zendikar,
/// {2}{R}{G}). Creature — Eldrazi Drone 2/3. Oracle text (Scryfall, verified):
///   "Devoid (This card has no color.)
///    When you cast this spell, create two 0/1 colorless Eldrazi Spawn
///    creature tokens with \"Sacrifice this token: Add {C}.\"
///    Reach
///    Whenever you sacrifice another Eldrazi, put a +1/+1 counter on this
///    creature."
///
/// Covers:
/// - Identity (Eldrazi Drone, mana cost, P/T, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Devoid marker (CR 702.114) — IsDevoid flag + keyword marker.
/// - Reach keyword marker (CR 702.17).
/// - Cast trigger shape (SpellCastEvent, activeZones = Stack).
/// - Cast-trigger effect creates two 0/1 colourless Eldrazi Spawn tokens.
/// - Sacrifice trigger (CardMovedEvent Battlefield -> Graveyard for another
///   Eldrazi) puts a +1/+1 counter on Writhing Chrysalis.
/// </summary>
[Trait("Color", "C")]
public class WrithingChrysalisFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WrithingChrysalis_Identity()
    {
        var card = WrithingChrysalisFactory.Create(_alice);

        card.Name.Should().Be("Writhing Chrysalis");
        card.ManaCost.Should().Be("{2}{R}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void WrithingChrysalis_IsDevoid_AndHasDevoidMarker()
    {
        var card = WrithingChrysalisFactory.Create(_alice);

        card.IsDevoid.Should().BeTrue("CR 702.114 — Devoid stamps the colourless flag");
        card.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Devoid")
            .Should().HaveCount(1);
    }

    [Fact]
    public void WrithingChrysalis_HasReachKeywordMarker()
    {
        var card = WrithingChrysalisFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Reach")
            .Should().HaveCount(1, "CR 702.17 — Reach is attached as a keyword marker");
    }

    [Fact]
    public void WrithingChrysalis_HasTwoTriggers()
    {
        var card = WrithingChrysalisFactory.Create(_alice);

        // Cast trigger (token creation) + sacrifice-another-Eldrazi trigger.
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Cast trigger — create two 0/1 colourless Eldrazi Spawn tokens.
    // -----------------------------------------------------------------------

    [Fact]
    public void WrithingChrysalis_CastTrigger_CreatesTwoEldraziSpawnTokens()
    {
        var card = WrithingChrysalisFactory.Create(_alice);

        // The cast trigger lives on the Stack (CR 603.6a — "When you cast").
        var castTrigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Stack));

        foreach (var e in castTrigger.Effects) e.Execute();

        var spawns = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Eldrazi Spawn")
            .ToList();

        spawns.Should().HaveCount(2, "the cast trigger creates two Eldrazi Spawn tokens");
        spawns.Should().OnlyContain(s => s.Power == 0 && s.Toughness == 1);
        spawns.Should().OnlyContain(s => s.IsToken);
        spawns.Should().OnlyContain(s => s.HasSubtype(CardSubtype.Eldrazi));
        spawns.Should().OnlyContain(s => s.HasSubtype(CardSubtype.Spawn));
        spawns.Should().OnlyContain(s => s.Abilities.OfType<ManaAbility>().Count() == 1);
    }

    // -----------------------------------------------------------------------
    // Sacrifice trigger — +1/+1 counter when another Eldrazi is sacrificed.
    // -----------------------------------------------------------------------

    [Fact]
    public void WrithingChrysalis_SacTrigger_FiresOnAnotherEldraziLeavingBattlefield()
    {
        var card = WrithingChrysalisFactory.Create(_alice);

        var sacTrigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Battlefield));

        var anotherEldrazi = new Creature(
            name: "Eldrazi Spawn",
            manaCost: "",
            power: 0,
            toughness: 1,
            subtypes: new[] { CardSubtype.Eldrazi, CardSubtype.Spawn });

        var evt = new CardMovedEvent(anotherEldrazi, ZoneType.Battlefield, ZoneType.Graveyard);

        sacTrigger.Condition.Matches(evt, sacTrigger).Should().BeTrue(
            "another Eldrazi leaving the battlefield satisfies the trigger");

        foreach (var e in sacTrigger.Effects) e.Execute();

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a +1/+1 counter is placed on Writhing Chrysalis");
    }

    [Fact]
    public void WrithingChrysalis_SacTrigger_DoesNotFireOnItself()
    {
        var card = WrithingChrysalisFactory.Create(_alice);

        var sacTrigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Battlefield));

        // "another Eldrazi" — the source itself does not count (CR 603.2).
        var evt = new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard);

        sacTrigger.Condition.Matches(evt, sacTrigger).Should().BeFalse(
            "Writhing Chrysalis sacrificing itself is not 'another Eldrazi'");
    }

    [Fact]
    public void WrithingChrysalis_SacTrigger_DoesNotFireOnNonEldrazi()
    {
        var card = WrithingChrysalisFactory.Create(_alice);

        var sacTrigger = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Battlefield));

        var goblin = new Creature(
            name: "Goblin",
            manaCost: "{R}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Goblin });

        var evt = new CardMovedEvent(goblin, ZoneType.Battlefield, ZoneType.Graveyard);

        sacTrigger.Condition.Matches(evt, sacTrigger).Should().BeFalse(
            "a non-Eldrazi leaving the battlefield does not trigger");
    }
}
