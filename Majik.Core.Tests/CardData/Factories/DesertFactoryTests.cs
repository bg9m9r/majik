using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DesertFactory"/> (Arabian Nights / numerous reprints).
/// Land — Desert:
///   "{T}: Add {C}.
///    {T}: This land deals 1 damage to target attacking creature. Activate
///    only during the end of combat step."
///
/// Shares the {C} Desert mana shape of <see cref="HostileDesertFactory"/> /
/// Sunscorched Desert, plus a {T} pinger restricted to a single target
/// attacking creature — the same any/target-creature + Fx.DealDamageAny damage
/// shape as <see cref="BarbarianRingFactory"/>'s sacrifice ability, narrowed to
/// a target attacking creature. The "Activate only during the end of combat
/// step" timing restriction (CR 602.5b) follows Barbarian Ring's threshold
/// posture: a public predicate (<see cref="DesertFactory.IsEndOfCombatStep"/>)
/// exposes the gate for bot-policy / action-validator use.
///
/// Covers:
/// - Identity (Land + Desert subtype, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {C} mana ability (one).
/// - The pinger ActivatedAbility: a {T} (AdditionalCost.Tap) cost, a single
///   1..1 target request, instant speed.
/// - Resolution deals 1 damage to a pre-chosen target creature.
/// - The end-of-combat timing predicate (CR 602.5b).
/// </summary>
[Trait("Color", "C")]
public class DesertFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Desert_Identity()
    {
        var land = DesertFactory.Create(_alice);

        land.Name.Should().Be("Desert");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue(
            "printed type line is \"Land — Desert\"");
        land.HasType(CardType.Creature).Should().BeFalse(
            "Desert is a plain land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Desert is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Desert_DispatchesThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Desert", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Desert");
    }

    // -----------------------------------------------------------------------
    // Abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void Desert_HasColorlessManaAndPingerAbility()
    {
        var land = DesertFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {T} deal-1-to-attacker pinger is wired");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Desert has no triggered ability");
    }

    [Fact]
    public void Desert_Pinger_HasTapCostAndSingleTargetInstantSpeed()
    {
        var land = DesertFactory.Create(_alice);

        var pinger = land.Abilities.OfType<ActivatedAbility>().Single();
        pinger.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            "the only cost is {T} (AdditionalCost.Tap)");
        pinger.TargetRequests.Should().ContainSingle(
            "the pinger targets exactly one attacking creature");
        pinger.TargetRequests[0].MinTargets.Should().Be(1);
        pinger.TargetRequests[0].MaxTargets.Should().Be(1);
        pinger.IsSorcerySpeed.Should().BeFalse(
            "the timing rider is end-of-combat-step, not sorcery speed");
    }

    // -----------------------------------------------------------------------
    // Damage resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Desert_Pinger_DealsOneDamageToChosenCreature()
    {
        var land = DesertFactory.Create(_alice);
        var goblin = new Creature("Goblin", "{R}", 2, 2);
        goblin.SetOwner(_alice);

        var pinger = land.Abilities.OfType<ActivatedAbility>().Single();
        pinger.SetChosenTargets(new[] { new object[] { goblin } });

        foreach (var e in pinger.Effects) e.Execute();

        goblin.Damage.Should().Be(DesertFactory.DamageAmount,
            "the pinger deals 1 damage to the chosen attacking creature");
    }

    [Fact]
    public void Desert_Pinger_NoTarget_IsNoOp()
    {
        var land = DesertFactory.Create(_alice);

        var pinger = land.Abilities.OfType<ActivatedAbility>().Single();

        // No chosen target → clean no-op (CR 608.2b). Must not throw.
        var act = () => { foreach (var e in pinger.Effects) e.Execute(); };
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Timing predicate — CR 602.5b "Activate only during the end of combat step"
    // -----------------------------------------------------------------------

    [Fact]
    public void Desert_IsEndOfCombatStep_OnlyTrueForEndOfCombat()
    {
        DesertFactory.IsEndOfCombatStep(StepStateType.EndOfCombat).Should().BeTrue();
        DesertFactory.IsEndOfCombatStep(StepStateType.DeclareAttackers).Should().BeFalse();
        DesertFactory.IsEndOfCombatStep(StepStateType.PostCombatMain).Should().BeFalse();
        DesertFactory.IsEndOfCombatStep(StepStateType.PreCombatMain).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Context-aware activation gate — CR 602.5b "Activate only during the end
    // of combat step". The pinger now carries a canActivateCheckCtx that reads
    // the live step off the GameContext (GameContext.CurrentPhase, the live
    // StepStateType), so AbilityActivator.CanActivate / the bot's
    // LegalActionEnumerator reject the activation outside the end-of-combat step
    // on the production routed build — not merely the public predicate.
    // -----------------------------------------------------------------------

    private GameContext ContextAtStep(StepStateType step) =>
        new(
            self: _alice,
            allPlayers: new[] { _alice },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: step,
            stack: new Majik.Core.Stack.Stack());

    [Fact]
    public void Desert_Pinger_CanActivateNow_TrueOnlyDuringEndOfCombatStep()
    {
        var land = DesertFactory.Create(_alice);
        var pinger = land.Abilities.OfType<ActivatedAbility>().Single();

        pinger.CanActivateNow(ContextAtStep(StepStateType.EndOfCombat)).Should().BeTrue(
            "CR 602.5b — the pinger may be activated during the end of combat step");
        pinger.CanActivateNow(ContextAtStep(StepStateType.DeclareAttackers)).Should().BeFalse(
            "the declare-attackers step is not the end of combat step");
        pinger.CanActivateNow(ContextAtStep(StepStateType.DeclareBlockers)).Should().BeFalse(
            "the declare-blockers step is not the end of combat step");
        pinger.CanActivateNow(ContextAtStep(StepStateType.CombatDamage)).Should().BeFalse(
            "the combat-damage step is not the end of combat step");
        pinger.CanActivateNow(ContextAtStep(StepStateType.PostCombatMain)).Should().BeFalse(
            "the post-combat main phase is not the end of combat step");
        pinger.CanActivateNow(ContextAtStep(StepStateType.Upkeep)).Should().BeFalse(
            "the upkeep step is not the end of combat step");
    }

    [Fact]
    public void Desert_Pinger_CanActivateNow_ContextLess_TrueForShapeTests()
    {
        // CR 602.5c — the context-less overload (no GameContext) can't reach the
        // step, so it falls back to "true" (the gate is context-aware only).
        // This keeps construction-only / shape tests and harnesses that don't
        // thread a GameContext from being wedged by the timing rider.
        var land = DesertFactory.Create(_alice);
        var pinger = land.Abilities.OfType<ActivatedAbility>().Single();

        pinger.CanActivateNow().Should().BeTrue(
            "the context-less consult has no step to gate on (CR 602.5c fallback)");
    }

    [Fact]
    public void Desert_Pinger_CanActivateCheckCtx_IsWired()
    {
        var land = DesertFactory.Create(_alice);
        var pinger = land.Abilities.OfType<ActivatedAbility>().Single();

        pinger.CanActivateCheckCtx.Should().NotBeNull(
            "the end-of-combat timing rider is modelled as the context-aware gate");
    }
}
