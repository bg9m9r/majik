using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LegionWarbossFactory"/> (Guilds of Ravnica, {1}{R}).
/// Creature — Goblin Soldier, 2/1:
///   "Mentor (Whenever this creature attacks, put a +1/+1 counter on target
///    attacking creature with lesser power.)
///    At the beginning of combat on your turn, create a 1/1 red Goblin
///    creature token. That token gains haste until end of turn and attacks
///    this combat if able."
///
/// Covers:
/// - Identity (Creature, Goblin + Soldier subtypes, {1}{R}, 2/1, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Mentor attack trigger: matches Warboss only (CR 508.1f self-match);
///   a 1..1 "target attacking creature with lesser power" TargetRequest;
///   resolution places a +1/+1 counter on a legal lesser-power attacker,
///   and is a no-op when the target's power is not strictly lesser.
/// - Begin-combat trigger: fires only on the controller's combat step;
///   creates a 1/1 red Goblin token with Haste (summoning sickness cleared)
///   carrying the "AttacksThisCombat" must-attack marker.
/// </summary>
public class LegionWarbossFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(
        Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, "R", power, toughness,
            subtypes: new[] { CardSubtype.Goblin });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetMentorTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    private static TriggeredAbility GetBeginCombatTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LegionWarboss_Identity()
    {
        var c = LegionWarbossFactory.Create(_alice);

        c.Name.Should().Be("Legion Warboss");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LegionWarboss_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Legion Warboss", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Legion Warboss");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    [Fact]
    public void LegionWarboss_HasMentorMarker()
    {
        var c = LegionWarbossFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Mentor").Should().BeTrue(
                "Mentor is a keyword ability (CR 702.134).");
    }

    // -----------------------------------------------------------------------
    // Mentor attack trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void LegionWarboss_MentorTrigger_MatchesSelfOnly()
    {
        var c = LegionWarbossFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetMentorTrigger(c);

        trigger.IsTriggered(new CreatureAttacksEvent(c, _bob)).Should().BeTrue(
            "CR 508.1f — Mentor's 'whenever this creature attacks' self-match.");

        var other = MakeCreature(_alice, "Mogg Fanatic", 1, 1);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the per-attacker Mentor trigger only fires for Warboss itself.");
    }

    [Fact]
    public void LegionWarboss_MentorTrigger_HasLesserPowerTargetRequest()
    {
        var c = LegionWarbossFactory.Create(_alice);
        var trigger = GetMentorTrigger(c);

        trigger.TargetRequests.Should().ContainSingle();
        var req = trigger.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("lesser power");
    }

    [Fact]
    public void LegionWarboss_MentorTrigger_PutsCounterOnLesserPowerAttacker()
    {
        // Warboss (power 2) attacking alongside a 1/1 Goblin token — the
        // 1-power attacker has lesser power and is a legal Mentor target.
        var attackers = new List<Creature>();
        var warboss = LegionWarbossFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        warboss.SetZone(ZoneType.Battlefield);

        var lesser = MakeCreature(_alice, "Goblin", 1, 1);
        attackers.AddRange(new[] { warboss, lesser });

        var trigger = GetMentorTrigger(warboss);
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { lesser } });
        foreach (var e in trigger.Effects) e.Execute();

        lesser.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 702.134 — a +1/+1 counter goes on the chosen lesser-power attacker.");
    }

    [Fact]
    public void LegionWarboss_MentorTrigger_NoCounterWhenTargetPowerNotLesser()
    {
        // A 2-power attacker is NOT a legal Mentor target for a 2-power
        // Warboss (CR 702.134a — strict "lesser power"). Resolution rechecks
        // legality (CR 608.2b) and places no counter.
        var attackers = new List<Creature>();
        var warboss = LegionWarbossFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        warboss.SetZone(ZoneType.Battlefield);

        var equalPower = MakeCreature(_alice, "Equal", 2, 2);
        attackers.AddRange(new[] { warboss, equalPower });

        var trigger = GetMentorTrigger(warboss);
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { equalPower } });
        foreach (var e in trigger.Effects) e.Execute();

        equalPower.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "power 2 is not strictly less than Warboss's power 2 — illegal target, no counter.");
    }

    [Fact]
    public void LegionWarboss_MentorTrigger_NoCounterWhenChosenTargetNotAttacking()
    {
        // A lesser-power creature that is NOT attacking is not a legal Mentor
        // target (CR 702.134 — "target attacking creature"). The resolve-time
        // recheck (CR 608.2b) drops it.
        var attackers = new List<Creature>();
        var warboss = LegionWarbossFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        warboss.SetZone(ZoneType.Battlefield);

        var notAttacking = MakeCreature(_alice, "Bench", 1, 1);
        attackers.Add(warboss); // notAttacking is NOT in the attackers list.

        var trigger = GetMentorTrigger(warboss);
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { notAttacking } });
        foreach (var e in trigger.Effects) e.Execute();

        notAttacking.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the chosen creature isn't attacking — illegal Mentor target.");
    }

    // -----------------------------------------------------------------------
    // Begin-combat token trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void LegionWarboss_BeginCombatTrigger_FiresOnControllerCombatStepOnly()
    {
        var c = LegionWarbossFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetBeginCombatTrigger(c);

        trigger.IsTriggered(
            new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice))
            .Should().BeTrue("fires at the beginning of combat on the controller's turn.");

        trigger.IsTriggered(
            new StepStartedEvent(PhaseStateType.BeginningOfCombat, _bob))
            .Should().BeFalse("'on your turn' — not on the opponent's combat.");

        trigger.IsTriggered(
            new StepStartedEvent(PhaseStateType.Upkeep, _alice))
            .Should().BeFalse("only the beginning-of-combat step triggers it.");
    }

    [Fact]
    public void LegionWarboss_BeginCombatTrigger_CreatesHastyRedGoblinToken()
    {
        var svc = new ContinuousEffectsService();

        var warboss = LegionWarbossFactory.Create(_alice);
        warboss.SetZone(ZoneType.Battlefield);
        warboss.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(warboss);

        var trigger = GetBeginCombatTrigger(warboss);
        foreach (var e in trigger.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.HasSubtype(CardSubtype.Goblin) && t.IsToken)
            .ToList();

        tokens.Should().HaveCount(1,
            "CR 111 — the begin-combat trigger creates exactly one 1/1 Goblin token.");
        var token = tokens[0];
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(token).Should().Contain(Majik.Core.ValueObjects.ManaColor.Red,
            "CR 111.4 — '1/1 red Goblin creature token'.");

        // "That token gains haste until end of turn" + summoning sickness
        // cleared so it can attack the same turn.
        CombatAbilities.HasHaste(token).Should().BeTrue(
            "the token gains Haste until end of turn.");
        token.HasSummoningSickness.Should().BeFalse(
            "Haste lifts summoning sickness for attack declaration (CR 702.10b).");

        // "and attacks this combat if able" — must-attack marker (primitive
        // deferred; same posture as Ulamog's Crusher).
        token.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "AttacksThisCombat").Should().BeTrue(
                "the must-attack requirement is recorded as a marker.");
    }
}
