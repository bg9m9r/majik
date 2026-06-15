using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MarwynTheNurturerFactory"/> (Dominaria — Legendary
/// Creature — Elf Druid {2}{G} 1/1).
///
/// Oracle text (verified against Scryfall):
///   "Whenever another Elf you control enters, put a +1/+1 counter on Marwyn.
///    {T}: Add an amount of {G} equal to Marwyn's power."
///
/// Covers (the card's UNIQUE behaviour):
/// - Identity (Legendary Elf Druid 1/1 {2}{G}).
/// - Counter trigger (CR 603.1): matches another controller-Elf entering, NOT
///   Marwyn herself, NOT a non-Elf, NOT an opponent's Elf, NOT a non-battlefield
///   move.
/// - Resolution puts a +1/+1 counter on Marwyn; multiple Elf-enters stack
///   (no once-per-turn lock).
/// - {T} mana ability scales with Marwyn's power (base + counters) — alone she
///   produces {G}; with two +1/+1 counters she produces {G}{G}{G}.
/// </summary>
[Trait("Color", "G")]
public class MarwynTheNurturerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeElf(Player owner, string name = "Llanowar Elves")
    {
        var c = new Creature(name, "G", 1, 1, subtypes: new[] { CardSubtype.Elf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonElf(Player owner)
    {
        var c = new Creature("Grizzly Bears", "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility CounterTrigger(Creature marwyn)
        => marwyn.Abilities.OfType<TriggeredAbility>().Single();

    // ── Identity ───────────────────────────────────────────────────────

    [Fact]
    public void Identity()
    {
        var c = MarwynTheNurturerFactory.Create(_alice);

        c.Name.Should().Be("Marwyn, the Nurturer");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasCounterTriggerAndManaAbility()
    {
        var c = MarwynTheNurturerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the other-Elf-enters +1/+1 counter trigger.");
        c.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {G} equal to Marwyn's power.");
    }

    // ── Counter trigger condition (CR 603.1) ────────────────────────────

    [Fact]
    public void CounterTrigger_Matches_OtherControllerElfEntering()
    {
        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(marwyn);
        var otherElf = MakeElf(_alice);
        var evt = new CardMovedEvent(otherElf, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeTrue(
            "another Elf you control entering triggers it (CR 603.1).");
    }

    [Fact]
    public void CounterTrigger_DoesNotMatch_Itself()
    {
        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(marwyn);
        var evt = new CardMovedEvent(marwyn, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "the printed 'another Elf' excludes Marwyn's own ETB.");
    }

    [Fact]
    public void CounterTrigger_DoesNotMatch_NonElf()
    {
        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(marwyn);
        var bears = MakeNonElf(_alice);
        var evt = new CardMovedEvent(bears, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "non-Elf creatures entering don't trigger the Elf-matters ability.");
    }

    [Fact]
    public void CounterTrigger_DoesNotMatch_OpponentElf()
    {
        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(marwyn);
        var bobElf = MakeElf(_bob, "Heritage Druid");
        var evt = new CardMovedEvent(bobElf, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "CR 109.5 — 'Elves you control' excludes the opponent's Elves.");
    }

    [Fact]
    public void CounterTrigger_DoesNotMatch_NonBattlefieldMove()
    {
        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(marwyn);
        var otherElf = MakeElf(_alice);
        var evt = new CardMovedEvent(otherElf, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "only entering the battlefield counts, not other zone changes.");
    }

    // ── Counter resolution (CR 122 / 121.2) ─────────────────────────────

    [Fact]
    public void CounterEffect_PutsPlusOnePlusOneCounterOnMarwyn()
    {
        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(marwyn);
        foreach (var e in trigger.Effects) e.Execute();

        marwyn.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "each other-Elf-enter puts one +1/+1 counter on Marwyn (CR 603.1).");
    }

    [Fact]
    public void CounterEffect_StacksAcrossMultipleElfEnters()
    {
        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);

        var trigger = CounterTrigger(marwyn);
        // Resolve twice — no once-per-turn lock (contrast Elvish Warmaster).
        foreach (var e in trigger.Effects) e.Execute();
        foreach (var e in trigger.Effects) e.Execute();

        marwyn.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "every other-Elf-enter adds a counter — no once-per-turn restriction.");
    }

    // ── {T} mana ability scales with power (CR 605.1 / 107.1b) ──────────

    [Fact]
    public void ManaAbility_AloneProducesOneGreen()
    {
        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(marwyn);
        // CR 302.6 — clear summoning sickness so the test exercises the
        // power-scaled mana count rather than the {T} sickness gate.
        marwyn.ClearSummoningSickness();

        var manaAbility = marwyn.Abilities.OfType<ManaAbility>().Single();
        manaAbility.CanActivate().Should().BeTrue();

        var mana = manaAbility.Activate();
        mana.ToString().Should().Be("G",
            "with no counters Marwyn's power is 1 → produces one green mana.");
        marwyn.IsTapped.Should().BeTrue("tap cost is paid on activation.");
    }

    [Fact]
    public void ManaAbility_ScalesWithCounterFedPower()
    {
        // +1/+1 counters raise power (CR 122.6); the layer system surfaces that
        // through GetPower only when a ContinuousEffectsService is wired (CR 613).
        var effects = new ContinuousEffectsService();

        var marwyn = MarwynTheNurturerFactory.Create(_alice);
        marwyn.SetZone(ZoneType.Battlefield);
        marwyn.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(marwyn);
        marwyn.ClearSummoningSickness();

        // Two other Elves enter → two +1/+1 counters → power 1 + 2 = 3.
        var trigger = CounterTrigger(marwyn);
        foreach (var e in trigger.Effects) e.Execute();
        foreach (var e in trigger.Effects) e.Execute();

        marwyn.GetPower().Should().Be(3,
            "1 base power + two +1/+1 counters (CR 122.6).");

        var manaAbility = marwyn.Abilities.OfType<ManaAbility>().Single();
        var mana = manaAbility.Activate();

        mana.ToString().Should().Be("GGG",
            "the mana ability adds {G} equal to Marwyn's (counter-fed) power.");
    }
}
