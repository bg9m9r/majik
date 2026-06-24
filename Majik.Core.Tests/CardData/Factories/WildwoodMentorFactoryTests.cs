using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WildwoodMentorFactory"/> (Bloomburrow, {2}{G}).
///
/// Wildwood Mentor — Creature — Treefolk 1/1. Oracle text (verified against
/// Scryfall):
///   "Whenever a token you control enters, put a +1/+1 counter on this
///    creature.
///    Whenever this creature attacks, another target attacking creature gets
///    +X/+X until end of turn, where X is this creature's power."
///
/// Coverage:
/// - Identity (Creature — Treefolk, 1/1, {2}{G}, green, owner/controller).
/// - Both triggered abilities attached.
/// - A token you control entering puts a +1/+1 counter on Wildwood Mentor
///   (CR 122.1); a NON-token creature ETB does NOT trigger; an opponent's
///   token does NOT trigger ("a token YOU control").
/// - The attack trigger targets "another target attacking creature" (mandatory
///   1..1, only attacking creatures offered) and pumps it +X/+X where X is
///   Wildwood Mentor's power READ AT RESOLUTION (CR 608.2h) — so accrued +1/+1
///   counters raise X. The pump expires at end of turn (CR 514.2).
/// </summary>
[Trait("Color", "G")]
public class WildwoodMentorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WildwoodMentor_Identity_CreatureTreefolk_1_1_Green2G()
    {
        var mentor = WildwoodMentorFactory.Create(_alice);

        mentor.Name.Should().Be("Wildwood Mentor");
        mentor.HasType(CardType.Creature).Should().BeTrue();
        mentor.ManaCost.Should().Be("{2}{G}");
        mentor.ManaCostValue.TotalValue.Should().Be(3);
        CardColors.GetColors(mentor).Should().Contain(ManaColor.Green);
        mentor.Power.Should().Be(1);
        mentor.Toughness.Should().Be(1);
        mentor.Subtypes.Should().Contain(CardSubtype.Treefolk);
        mentor.Owner.Should().BeSameAs(_alice);
        mentor.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WildwoodMentor_HasBothTriggers()
    {
        var mentor = WildwoodMentorFactory.Create(_alice);

        mentor.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one token-ETB counter trigger and one attack pump trigger");
    }

    // -----------------------------------------------------------------------
    // Token-ETB counter trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void WildwoodMentor_TokenYouControlEnters_PutsPlusOneCounter()
    {
        var (zones, stack, triggers) = BuildEngine();

        var effects = new ContinuousEffectsService();
        var mentor = WildwoodMentorFactory.Create(_alice, triggers, effects);
        // Counters feed power/toughness only through the layers service
        // (ApplyCounterPostlude), wired here as the production GameFacade path does.
        mentor.ActiveEffects = effects;
        PlaceOnBattlefield(mentor, _alice);
        triggers.BindCard(mentor);

        // A token entering under Alice's control.
        TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Squirrel", 1, 1, new[] { CardSubtype.Squirrel },
                Colors: new[] { ManaColor.Green }),
            _alice, zones);

        triggers.PendingCount.Should().Be(1,
            "a token you control entering queues the counter trigger");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        mentor.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        mentor.Power.Should().Be(2, "the +1/+1 counter raises power to 2");
        mentor.Toughness.Should().Be(2, "the +1/+1 counter raises toughness to 2");
    }

    [Fact]
    public void WildwoodMentor_NonTokenCreatureEnters_DoesNotTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var mentor = WildwoodMentorFactory.Create(_alice, triggers, effects: null);
        PlaceOnBattlefield(mentor, _alice);
        triggers.BindCard(mentor);

        // A real (non-token) creature entering does NOT trigger.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bear);
        zones.MoveCardTo(bear, ZoneType.Battlefield, _alice);

        triggers.PendingCount.Should().Be(0,
            "only a TOKEN entering triggers the counter ability");
    }

    [Fact]
    public void WildwoodMentor_OpponentsTokenEnters_DoesNotTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var mentor = WildwoodMentorFactory.Create(_alice, triggers, effects: null);
        PlaceOnBattlefield(mentor, _alice);
        triggers.BindCard(mentor);

        // A token entering under the OPPONENT's control does NOT trigger.
        TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec("Goblin", 1, 1, Colors: new[] { ManaColor.Red }),
            _bob, zones);

        triggers.PendingCount.Should().Be(0,
            "the trigger requires a token YOU control (CR 109.4)");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — pump another attacking creature by X (= mentor power)
    // -----------------------------------------------------------------------

    [Fact]
    public void WildwoodMentor_AttackTrigger_TargetsAnotherAttackingCreature_1To1()
    {
        var mentor = WildwoodMentorFactory.Create(_alice);

        var attackTrigger = mentor.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Any());
        var req = attackTrigger.TargetRequests.Single();
        req.MinTargets.Should().Be(1, "another target attacking creature — mandatory");
        req.MaxTargets.Should().Be(1, "a single target");
    }

    [Fact]
    public void WildwoodMentor_AttackTrigger_PumpsTargetByMentorPower_UntilEndOfTurn()
    {
        using var combatScope = CombatMembershipRegistryProvider.PushScope();
        var effects = new ContinuousEffectsService();

        var mentor = WildwoodMentorFactory.Create(_alice, triggers: null, effects);
        PlaceOnBattlefield(mentor, _alice);

        // Another attacking creature controlled by Alice.
        var ally = new Creature("Ally", "{2}{G}", 3, 3);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        PlaceOnBattlefield(ally, _alice);

        // Both are declared attackers this combat (CR 508.1).
        CombatMembershipRegistryProvider.Current.RecordAttacker(mentor);
        CombatMembershipRegistryProvider.Current.RecordAttacker(ally);

        var attackTrigger = mentor.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Any());
        attackTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ally }
        });
        foreach (var e in attackTrigger.Effects) e.Execute();

        // X = mentor's power = 1, so ally (base 3/3) becomes 4/4.
        var chars = effects.Compute(ally);
        chars.Power.Should().Be(4, "ally gets +1/+1 (X = mentor power 1)");
        chars.Toughness.Should().Be(4);

        // CR 514.2 — the pump expires at end of turn.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(ally);
        after.Power.Should().Be(3, "the +X/+X pump expired at end of turn");
        after.Toughness.Should().Be(3);
    }

    [Fact]
    public void WildwoodMentor_AttackTrigger_XReadAtResolution_CountsCounters()
    {
        using var combatScope = CombatMembershipRegistryProvider.PushScope();
        var effects = new ContinuousEffectsService();

        var mentor = WildwoodMentorFactory.Create(_alice, triggers: null, effects);
        // Counters feed power only through the layers service (production path).
        mentor.ActiveEffects = effects;
        PlaceOnBattlefield(mentor, _alice);
        // Two +1/+1 counters from prior token ETBs make Wildwood Mentor a 3/3.
        mentor.Counters.Add(CounterType.PlusOnePlusOne, 2);
        mentor.Power.Should().Be(3, "1/1 base + two +1/+1 counters");

        var ally = new Creature("Ally", "{2}{G}", 2, 2);
        ally.SetOwner(_alice);
        ally.SetController(_alice);
        PlaceOnBattlefield(ally, _alice);

        CombatMembershipRegistryProvider.Current.RecordAttacker(mentor);
        CombatMembershipRegistryProvider.Current.RecordAttacker(ally);

        var attackTrigger = mentor.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Any());
        attackTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { ally }
        });
        foreach (var e in attackTrigger.Effects) e.Execute();

        // CR 608.2h — X = mentor's power AT RESOLUTION = 3, so ally (2/2) → 5/5.
        var chars = effects.Compute(ally);
        chars.Power.Should().Be(5, "X = mentor's power (3) including its +1/+1 counters");
        chars.Toughness.Should().Be(5);
    }

    [Fact]
    public void WildwoodMentor_AttackTrigger_CandidatePool_ExcludesSelf_AndNonAttackers()
    {
        using var combatScope = CombatMembershipRegistryProvider.PushScope();

        var mentor = WildwoodMentorFactory.Create(_alice);
        PlaceOnBattlefield(mentor, _alice);

        var attacker = new Creature("Attacker", "{G}", 1, 1);
        attacker.SetOwner(_alice);
        attacker.SetController(_alice);
        PlaceOnBattlefield(attacker, _alice);

        var bystander = new Creature("Bystander", "{G}", 1, 1);
        bystander.SetOwner(_alice);
        bystander.SetController(_alice);
        PlaceOnBattlefield(bystander, _alice);

        // Mentor and one ally attack; the bystander does NOT.
        CombatMembershipRegistryProvider.Current.RecordAttacker(mentor);
        CombatMembershipRegistryProvider.Current.RecordAttacker(attacker);

        var attackTrigger = mentor.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Any());
        var pool = attackTrigger.TargetRequests.Single().CandidateGatherer!(null!)
            .Cast<Creature>().ToList();

        pool.Should().Contain(attacker, "an OTHER attacking creature is a legal target");
        pool.Should().NotContain(mentor, "\"another\" excludes Wildwood Mentor itself");
        pool.Should().NotContain(bystander, "a non-attacking creature is not a legal target");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Permanent card, Player controller)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
