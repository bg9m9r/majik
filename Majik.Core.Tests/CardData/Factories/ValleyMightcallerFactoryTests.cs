using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ValleyMightcallerFactory"/> (Bloomburrow — Creature —
/// Frog Warrior {G} 1/1).
///
/// Oracle text (verified against Scryfall):
///   "Trample
///    Whenever another Frog, Rabbit, Raccoon, or Squirrel you control enters,
///    put a +1/+1 counter on this creature."
///
/// Covers (the card's UNIQUE behaviour):
/// - Identity (Frog Warrior 1/1 {G}, Trample keyword).
/// - Counter trigger (CR 603.1): matches another controller Frog/Rabbit/Raccoon/
///   Squirrel entering, NOT Mightcaller herself, NOT an off-type creature, NOT an
///   opponent's matching creature, NOT a non-battlefield move.
/// - Resolution puts a +1/+1 counter on Mightcaller; multiple matching enters
///   stack (no once-per-turn lock).
/// </summary>
[Trait("Color", "G")]
public class ValleyMightcallerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, CardSubtype subtype, string name)
    {
        var c = new Creature(name, "G", 1, 1, subtypes: new[] { subtype });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeOffType(Player owner)
    {
        var c = new Creature("Grizzly Bears", "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility CounterTrigger(Creature mightcaller)
        => mightcaller.Abilities.OfType<TriggeredAbility>().Single();

    // ── Identity ───────────────────────────────────────────────────────

    [Fact]
    public void Identity()
    {
        var c = ValleyMightcallerFactory.Create(_alice);

        c.Name.Should().Be("Valley Mightcaller");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Frog).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "CR 702.19 — Valley Mightcaller has Trample (printed keyword).");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasExactlyOneCounterTrigger()
    {
        var c = ValleyMightcallerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the other-Frog/Rabbit/Raccoon/Squirrel-enters +1/+1 counter trigger.");
    }

    // ── Counter trigger condition (CR 603.1) ────────────────────────────

    [Theory]
    [InlineData(CardSubtype.Frog)]
    [InlineData(CardSubtype.Rabbit)]
    [InlineData(CardSubtype.Raccoon)]
    [InlineData(CardSubtype.Squirrel)]
    public void CounterTrigger_Matches_EachOfTheFourSubtypes(CardSubtype subtype)
    {
        var mightcaller = ValleyMightcallerFactory.Create(_alice);
        mightcaller.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(mightcaller);
        var other = MakeCreature(_alice, subtype, $"Token-{subtype}");
        var evt = new CardMovedEvent(other, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeTrue(
            $"another {subtype} you control entering triggers it (CR 603.1).");
    }

    [Fact]
    public void CounterTrigger_DoesNotMatch_Itself()
    {
        var mightcaller = ValleyMightcallerFactory.Create(_alice);
        mightcaller.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(mightcaller);
        var evt = new CardMovedEvent(mightcaller, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "the printed 'another' excludes Mightcaller's own ETB even though she is a Frog.");
    }

    [Fact]
    public void CounterTrigger_DoesNotMatch_OffTypeCreature()
    {
        var mightcaller = ValleyMightcallerFactory.Create(_alice);
        mightcaller.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(mightcaller);
        var bears = MakeOffType(_alice);
        var evt = new CardMovedEvent(bears, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "a creature with none of the four matched subtypes does not trigger it.");
    }

    [Fact]
    public void CounterTrigger_DoesNotMatch_OpponentMatchingCreature()
    {
        var mightcaller = ValleyMightcallerFactory.Create(_alice);
        mightcaller.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(mightcaller);
        var bobSquirrel = MakeCreature(_bob, CardSubtype.Squirrel, "Bob's Squirrel");
        var evt = new CardMovedEvent(bobSquirrel, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "CR 109.5 — 'you control' excludes the opponent's matching creatures.");
    }

    [Fact]
    public void CounterTrigger_DoesNotMatch_NonBattlefieldMove()
    {
        var mightcaller = ValleyMightcallerFactory.Create(_alice);
        mightcaller.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(mightcaller);
        var other = MakeCreature(_alice, CardSubtype.Rabbit, "Rabbit");
        var evt = new CardMovedEvent(other, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "only entering the battlefield counts, not other zone changes.");
    }

    // ── Counter resolution (CR 122 / 121.2) ─────────────────────────────

    [Fact]
    public void CounterEffect_PutsPlusOnePlusOneCounter()
    {
        var mightcaller = ValleyMightcallerFactory.Create(_alice);
        mightcaller.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(mightcaller);
        foreach (var e in trigger.Effects) e.Execute();

        mightcaller.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "each matching other-creature-enter puts one +1/+1 counter on Mightcaller (CR 603.1).");
    }

    [Fact]
    public void CounterEffect_StacksAcrossMultipleEnters()
    {
        var mightcaller = ValleyMightcallerFactory.Create(_alice);
        mightcaller.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(mightcaller);
        // Resolve twice — no once-per-turn lock.
        foreach (var e in trigger.Effects) e.Execute();
        foreach (var e in trigger.Effects) e.Execute();

        mightcaller.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "every matching enter adds a counter — no once-per-turn restriction.");
    }
}
