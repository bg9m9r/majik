using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BerserkFactory"/>.
///
/// Card: Berserk — Instant {G} (Limited Edition Alpha).
///   "Cast this spell only before the combat damage step.
///    Target creature gains trample and gets +X/+0 until end of turn,
///    where X is its power.
///    At the beginning of the next end step, destroy that creature if
///    it attacked this turn."
///
/// Covers:
///   - Identity (Instant, green, {G}).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape (1 target creature request, no modes, no X).
///   - Resolve grants Trample EOT.
///   - Resolve doubles power: +X/+0 where X = current power.
///   - End-of-turn cleanup lifts Trample + pump (CR 514.2).
///   - Fizzle: target not on battlefield → no-op (CR 608.2b).
///   - Delayed end-step destroy fires when the target attacked.
///   - No destroy when the target never attacked.
///
/// ## v1 gap
/// - The "cast only before the combat damage step" timing restriction is
///   documented but not enforced (engine has no generic timing-restriction
///   hook on instant cast-flow yet).
/// </summary>
[Trait("Color", "G")]
public class BerserkFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Green_AtCostG()
    {
        var b = BerserkFactory.Create(_alice);

        b.Name.Should().Be("Berserk");
        b.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(b).Should().Contain(ManaColor.Green);
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
        b.ManaCost.Should().Be("{G}");
    }
    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_SingleTargetCreatureRequest()
    {
        var def = BerserkFactory.BuildSpellDefinition(t => t);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── Pump + Trample grant ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_GrantsTrample_AndDoublesPower()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);

        CombatAbilities.HasTrample(bear).Should().BeFalse();

        ExecuteResolve(bear);

        CombatAbilities.HasTrample(bear).Should().BeTrue(
            "CR 702.19 — Berserk grants Trample until end of turn");
        bear.GetPower().Should().Be(4,
            "CR 613.4d — +X/+0 where X = power at start of resolution (2)");
        bear.GetToughness().Should().Be(2,
            "Berserk's pump is power-only (+X/+0)");
    }

    [Fact]
    public void Resolve_EndOfTurnCleanup_LiftsTrampleAndPump()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 3, toughness: 3);

        ExecuteResolve(bear);
        CombatAbilities.HasTrample(bear).Should().BeTrue();
        bear.GetPower().Should().Be(6);

        // CR 514.2 — EOT-flagged effects expire at cleanup.
        continuous.ExpireEndOfTurn();

        CombatAbilities.HasTrample(bear).Should().BeFalse();
        bear.GetPower().Should().Be(3);
        bear.GetToughness().Should().Be(3);
    }

    [Fact]
    public void Resolve_TargetNotOnBattlefield_IsNoOp()
    {
        var continuous = new ContinuousEffectsService();
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Graveyard,
            ActiveEffects = continuous,
        };
        _bob.Zones.Graveyard.AddCard(bear);

        ExecuteResolve(bear);

        CombatAbilities.HasTrample(bear).Should().BeFalse();
        bear.GetPower().Should().Be(2,
            "CR 608.2b — illegal target → no-op");
    }

    [Fact]
    public void Resolve_NonCreatureResolverResult_IsNoOp()
    {
        var nonCreature = new Card("Mountain Token", "");
        var def = BerserkFactory.BuildSpellDefinition(_ => nonCreature);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { nonCreature } },
            Mana: ManaPayment.Empty);

        // CR 608.2b — non-Creature resolver result → effect resolves as no-op.
        // Contract: must not throw.
        var act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };
        act.Should().NotThrow();
    }

    // ── Delayed end-step destroy ──────────────────────────────────────────────

    [Fact]
    public void EndStep_DestroysCreature_WhenItAttackedAfterResolve()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);

        // Resolve Berserk targeting the bear, wired to a live bus + triggers.
        ExecuteResolve(bear, _bus, triggers);

        // The bear attacks Bob (CR 508.1f).
        _bus.Publish(new CreatureAttacksEvent(bear, _bob));

        // Next end step — delayed destroy trigger matches.
        // Wait so the StepStartedEvent timestamp is strictly after the
        // resolve-time fence (DateTime.UtcNow comparison).
        Thread.Sleep(2);
        _bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 603.7 / 701.7 — delayed end-step destroy fires because the creature attacked this turn");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void EndStep_DoesNotDestroy_WhenCreatureDidNotAttack()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);

        ExecuteResolve(bear, _bus, triggers);

        // No CreatureAttacksEvent — the bear sat back.

        Thread.Sleep(2);
        _bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Berserk's destroy clause is conditional — no attack, no destroy");
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void EndStep_DoesNotDestroyOtherAttackers()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);
        var other = BuildCreature(new ContinuousEffectsService(), _alice, power: 3, toughness: 3);

        ExecuteResolve(bear, _bus, triggers);

        // Some unrelated creature attacks — must not flip Berserk's flag.
        _bus.Publish(new CreatureAttacksEvent(other, _bob));

        Thread.Sleep(2);
        _bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        var resolver = new StackResolver(_bus, zones);
        while (!stack.IsEmpty) resolver.ResolveTop(stack);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Only the Berserk-targeted creature's own attacks count");
    }

    // ── CR 601.3 self cast-timing restriction ──────────────────────────────────

    [Fact]
    public void Create_StampsCastTimingRestriction_AllowingOnlyBeforeCombatDamage()
    {
        var b = BerserkFactory.Create(_alice);

        b.CastTimingRestriction.Should().NotBeNull(
            "CR 601.3 — Berserk bakes a self cast-timing restriction onto the card");

        // Every step strictly before the combat damage step is legal.
        foreach (var step in new[]
        {
            StepStateType.Untap, StepStateType.Upkeep, StepStateType.Draw,
            StepStateType.PreCombatMain, StepStateType.BeginningOfCombat,
            StepStateType.DeclareAttackers, StepStateType.DeclareBlockers,
        })
        {
            b.CastTimingRestriction!(step).Should().BeTrue(
                $"casting Berserk is legal during the {step} step (before combat damage)");
        }

        // The combat damage step and every later step are illegal.
        foreach (var step in new[]
        {
            StepStateType.CombatDamage, StepStateType.EndOfCombat,
            StepStateType.PostCombatMain, StepStateType.End, StepStateType.Cleanup,
        })
        {
            b.CastTimingRestriction!(step).Should().BeFalse(
                $"casting Berserk is illegal during the {step} step (combat damage or later)");
        }
    }

    [Fact]
    public async Task CastFlow_BeforeCombatDamage_PutsBerserkOnStack()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);

        var berserk = BerserkFactory.Create(_alice);
        berserk.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(berserk);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            activePlayer: _alice, 1, StepStateType.DeclareBlockers, stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        await flow.CastAsync(_alice, berserk,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        stack.Count.Should().Be(1,
            "CR 601.3 — Berserk may be cast on any step before the combat damage step");
    }

    [Fact]
    public async Task CastFlow_OnCombatDamageStep_RejectsBerserk()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);

        var berserk = BerserkFactory.Create(_alice);
        berserk.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(berserk);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            activePlayer: _alice, 1, StepStateType.CombatDamage, stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var act = async () => await flow.CastAsync(_alice, berserk,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*cast-timing restriction*");
        stack.Count.Should().Be(0,
            "CR 601.3 — Berserk can't be cast during the combat damage step");
        berserk.Zone.Should().Be(ZoneType.Hand,
            "CR 731.1 — a rejected hand cast is rewound off the stack");
    }

    [Fact]
    public async Task CastFlow_AfterCombatDamage_RejectsBerserk()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new ZoneService(_bus);
        var flow = new SpellCastFlow(stack, zones, _bus);

        var berserk = BerserkFactory.Create(_alice);
        berserk.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(berserk);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            activePlayer: _alice, 1, StepStateType.PostCombatMain, stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var act = async () => await flow.CastAsync(_alice, berserk,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*cast-timing restriction*");
        stack.Count.Should().Be(0);
    }

    [Fact]
    public void Validator_HonoursSelfCastTimingRestriction_WhenStepStamped()
    {
        var validator = new Majik.Core.Rules.ActionValidator();
        var berserk = BerserkFactory.Create(_alice);

        // Before combat damage — legal.
        var early = new Majik.Core.Rules.CastSpellAction(
            berserk, _alice, sorcerySpeedAvailable: true, fromZone: null, targets: null,
            targetSpec: null, currentStep: StepStateType.DeclareAttackers);
        validator.ValidateAction(early).IsValid.Should().BeTrue();

        // Combat damage step — illegal.
        var late = new Majik.Core.Rules.CastSpellAction(
            berserk, _alice, sorcerySpeedAvailable: true, fromZone: null, targets: null,
            targetSpec: null, currentStep: StepStateType.CombatDamage);
        var result = validator.ValidateAction(late);
        result.IsValid.Should().BeFalse(
            "CR 601.3 — the validator honours the self cast-timing restriction when the step is stamped");
        result.ErrorMessage.Should().Contain("CombatDamage");
    }

    [Fact]
    public void Validator_NoStepStamped_LeavesSelfTimingGateUnenforced()
    {
        // Legacy callers that don't stamp CurrentStep keep the prior posture —
        // the self-timing gate is skipped (no false rejections).
        var validator = new Majik.Core.Rules.ActionValidator();
        var berserk = BerserkFactory.Create(_alice);

        var action = new Majik.Core.Rules.CastSpellAction(
            berserk, _alice, sorcerySpeedAvailable: true);
        validator.ValidateAction(action).IsValid.Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ExecuteResolve(
        Creature target,
        IEventBus? bus = null,
        TriggerManager? triggers = null)
    {
        var def = BerserkFactory.BuildSpellDefinition(t => t, bus, triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private Creature BuildCreature(
        ContinuousEffectsService continuous,
        Player controller,
        int power,
        int toughness)
    {
        var c = new Creature($"{power}/{toughness} Bear", "{G}", power, toughness)
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }
}
