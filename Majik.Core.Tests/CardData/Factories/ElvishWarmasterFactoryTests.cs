using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ElvishWarmasterFactory"/> (Kaldheim Commander —
/// Creature — Elf Warrior {1}{G} 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Whenever one or more other Elves you control enter, create a 1/1 green
///    Elf Warrior creature token. This ability triggers only once each turn.
///    {5}{G}{G}: Elves you control get +2/+2 and gain deathtouch until end of
///    turn."
///
/// Covers:
/// - Identity + named-factory dispatch (Elf Warrior 2/2 {1}{G}).
/// - Token trigger (CR 603.1): matches another controller-Elf entering, NOT
///   the Warmaster itself, NOT a non-Elf, NOT an opponent's Elf, NOT a
///   non-battlefield move.
/// - "Triggers only once each turn" (CR 603.2c): the lock closes on the first
///   Elf-enter of the turn and reopens on TurnStartedEvent.
/// - Token resolution mints a 1/1 green Elf Warrior under the controller.
/// - {5}{G}{G} overrun (CR 602 / 613): every Elf the controller controls gets
///   +2/+2 and gains Deathtouch until end of turn; non-Elves + opponent Elves
///   are untouched.
/// </summary>
[Trait("Color", "G")]
public class ElvishWarmasterFactoryTests
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

    private static Creature MakeNonElf(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility TokenTrigger(Creature warmaster)
        => warmaster.Abilities.OfType<TriggeredAbility>().Single();

    private static ActivatedAbility OverrunAbility(Creature warmaster)
        => warmaster.Abilities.OfType<ActivatedAbility>().Single();

    // ── Identity ───────────────────────────────────────────────────────

    [Fact]
    public void Identity()
    {
        var c = ElvishWarmasterFactory.Create(_alice);

        c.Name.Should().Be("Elvish Warmaster");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasTokenTriggerAndOverrunAbility()
    {
        var c = ElvishWarmasterFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the once-per-turn Elf-enters token trigger");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {5}{G}{G} overrun");
    }

    [Fact]
    public void DispatchesViaNamedFactory()
    {
        var card = NamedCardFactory.Create("Elvish Warmaster", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Elvish Warmaster");
    }

    // ── Token trigger condition (CR 603.1) ──────────────────────────────

    [Fact]
    public void TokenTrigger_Matches_OtherControllerElfEntering()
    {
        var warmaster = ElvishWarmasterFactory.Create(_alice);
        warmaster.SetZone(ZoneType.Battlefield);

        var trigger = TokenTrigger(warmaster);
        var otherElf = MakeElf(_alice);
        var evt = new CardMovedEvent(otherElf, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeTrue(
            "another Elf you control entering the battlefield triggers it (CR 603.1).");
    }

    [Fact]
    public void TokenTrigger_DoesNotMatch_Itself()
    {
        var warmaster = ElvishWarmasterFactory.Create(_alice);
        warmaster.SetZone(ZoneType.Battlefield);

        var trigger = TokenTrigger(warmaster);
        var evt = new CardMovedEvent(warmaster, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "the printed 'other Elves' excludes the Warmaster itself entering.");
    }

    [Fact]
    public void TokenTrigger_DoesNotMatch_NonElf()
    {
        var warmaster = ElvishWarmasterFactory.Create(_alice);
        warmaster.SetZone(ZoneType.Battlefield);

        var trigger = TokenTrigger(warmaster);
        var bears = MakeNonElf(_alice);
        var evt = new CardMovedEvent(bears, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "non-Elf creatures entering don't trigger the Elf-matters ability.");
    }

    [Fact]
    public void TokenTrigger_DoesNotMatch_OpponentElf()
    {
        var warmaster = ElvishWarmasterFactory.Create(_alice);
        warmaster.SetZone(ZoneType.Battlefield);

        var trigger = TokenTrigger(warmaster);
        var bobElf = MakeElf(_bob, "Heritage Druid");
        var evt = new CardMovedEvent(bobElf, ZoneType.Stack, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "CR 109.5 — 'Elves you control' excludes the opponent's Elves.");
    }

    [Fact]
    public void TokenTrigger_DoesNotMatch_NonBattlefieldMove()
    {
        var warmaster = ElvishWarmasterFactory.Create(_alice);
        warmaster.SetZone(ZoneType.Battlefield);

        var trigger = TokenTrigger(warmaster);
        var otherElf = MakeElf(_alice);
        // Elf moving to the graveyard — not an "enter the battlefield".
        var evt = new CardMovedEvent(otherElf, ZoneType.Battlefield, ZoneType.Graveyard);

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "only entering the battlefield counts, not other zone changes.");
    }

    // ── "Triggers only once each turn" (CR 603.2c) ──────────────────────

    [Fact]
    public void TokenTrigger_OnlyOnceEachTurn()
    {
        var warmaster = ElvishWarmasterFactory.Create(_alice);
        warmaster.SetZone(ZoneType.Battlefield);

        var trigger = TokenTrigger(warmaster);

        var elf1 = MakeElf(_alice, "Llanowar Elves");
        var elf2 = MakeElf(_alice, "Elvish Mystic");

        // First Elf-enter of the turn triggers.
        trigger.Condition.Matches(
            new CardMovedEvent(elf1, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeTrue("first other-Elf-enter of the turn triggers.");

        // Second Elf-enter the same turn does NOT — the lock is closed.
        trigger.Condition.Matches(
            new CardMovedEvent(elf2, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeFalse("CR 603.2c — the ability triggers only once each turn.");
    }

    [Fact]
    public void TokenTrigger_LockResetsOnNewTurn()
    {
        var bus = new EventBus();
        var warmaster = ElvishWarmasterFactory.Create(
            _alice, eventBus: bus, triggers: null, zoneService: null);
        warmaster.SetZone(ZoneType.Battlefield);

        var trigger = TokenTrigger(warmaster);
        var elf1 = MakeElf(_alice, "Llanowar Elves");
        var elf2 = MakeElf(_alice, "Elvish Mystic");

        trigger.Condition.Matches(
            new CardMovedEvent(elf1, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeTrue();
        trigger.Condition.Matches(
            new CardMovedEvent(elf2, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeFalse("lock closed for the rest of this turn.");

        // CR 500.1 — start of a new turn reopens the lock.
        bus.Publish(new TurnStartedEvent(_alice, 2));

        trigger.Condition.Matches(
            new CardMovedEvent(elf2, ZoneType.Stack, ZoneType.Battlefield), trigger)
            .Should().BeTrue("the once-per-turn lock resets at the start of each turn.");
    }

    // ── Token resolution (CR 111 / 111.4) ───────────────────────────────

    [Fact]
    public void TokenEffect_MintsGreenElfWarriorUnderController()
    {
        var warmaster = ElvishWarmasterFactory.Create(_alice);
        warmaster.SetZone(ZoneType.Battlefield);

        var before = _alice.Zones.Battlefield.GetCards().Count();

        var trigger = TokenTrigger(warmaster);
        foreach (var e in trigger.Effects) e.Execute();

        var battlefield = _alice.Zones.Battlefield.GetCards().ToList();
        battlefield.Count.Should().Be(before + 1, "one Elf Warrior token created.");

        var token = battlefield.OfType<Creature>().Single(c => c.IsToken);
        token.Name.Should().Be("Elf Warrior");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        token.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        CardColors.GetColors(token).Should().Contain(ManaColor.Green,
            "printed '1/1 green Elf Warrior creature token' (CR 111.4).");
        token.Controller.Should().BeSameAs(_alice);
    }

    // ── {5}{G}{G} overrun (CR 602 / 613) ────────────────────────────────

    [Fact]
    public void Overrun_HasManaCost()
    {
        var ability = OverrunAbility(ElvishWarmasterFactory.Create(_alice));

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Description.Should().Contain("5",
                "the overrun costs {5}{G}{G}.");
    }

    [Fact]
    public void Overrun_PumpsElvesAndGrantsDeathtouch_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();

        var warmaster = ElvishWarmasterFactory.Create(_alice);
        warmaster.SetZone(ZoneType.Battlefield);
        warmaster.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(warmaster);

        var friendElf = MakeElf(_alice, "Llanowar Elves");
        friendElf.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(friendElf);

        var bears = MakeNonElf(_alice);
        bears.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(bears);

        var bobElf = MakeElf(_bob, "Heritage Druid");
        bobElf.ActiveEffects = effects;
        _bob.Zones.Battlefield.AddCard(bobElf);

        // Resolve the overrun.
        var ability = OverrunAbility(warmaster);
        foreach (var e in ability.Effects) e.Execute();

        // Controller's Elves: +2/+2 + Deathtouch.
        warmaster.GetPower().Should().Be(4, "2/2 base +2/+2.");
        warmaster.GetToughness().Should().Be(4);
        CombatAbilities.HasDeathtouch(warmaster).Should().BeTrue();

        friendElf.GetPower().Should().Be(3, "1/1 base +2/+2.");
        friendElf.GetToughness().Should().Be(3);
        CombatAbilities.HasDeathtouch(friendElf).Should().BeTrue();

        // Non-Elf you control: untouched.
        bears.GetPower().Should().Be(2);
        bears.GetToughness().Should().Be(2);
        CombatAbilities.HasDeathtouch(bears).Should().BeFalse(
            "the pump is scoped to Elves only.");

        // Opponent's Elf: untouched (CR 109.5 — 'Elves you control').
        bobElf.GetPower().Should().Be(1);
        bobElf.GetToughness().Should().Be(1);
        CombatAbilities.HasDeathtouch(bobElf).Should().BeFalse();
    }

    [Fact]
    public void Overrun_NoElves_NoOpsCleanly()
    {
        var warmaster = ElvishWarmasterFactory.Create(_alice);
        var ability = OverrunAbility(warmaster);

        var act = () => { foreach (var e in ability.Effects) e.Execute(); };
        act.Should().NotThrow();
    }
}
