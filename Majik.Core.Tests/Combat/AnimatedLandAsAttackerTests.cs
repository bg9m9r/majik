using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Combat;

/// <summary>
/// Deferral <c>animated-noncreature-as-combatant</c> (4B) — the ATTACKER side.
/// An animated manland (a <see cref="Land"/> C# instance computing as a creature
/// via a Layer-4 type grant — CR 613.1c) can now be DECLARED as an attacker and
/// deal combat damage, fire its per-attacker <see cref="CreatureAttacksEvent"/>
/// (so Restless-land "whenever ~ attacks" triggers finally observe their own
/// land), take combat damage back, and die to the lethal-damage SBA — all
/// without re-instancing the Land as a <see cref="Creature"/> (the trap the
/// deferral calls out). The combat subsystem reads its body through the
/// already-lifted <see cref="Permanent"/>-level surface
/// (<see cref="Permanent.GetEffectivePower"/> / <see cref="Permanent.MarkDamage"/>
/// / <see cref="Permanent.HasLethalMarkedDamage"/>).
///
/// <para>A real <see cref="Creature"/> still routes identically (it overrides
/// every member of that surface to read its own authoritative fields), so this
/// widening is invisible to ordinary creature combat — the regression facts
/// pin that.</para>
/// </summary>
public class AnimatedLandAsAttackerTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public AnimatedLandAsAttackerTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    private static Creature NewCreature(string name, int p, int t, Player owner) =>
        new(name, "1", p, t) { Owner = owner, Controller = owner };

    /// <summary>
    /// Celestial Colonnade — "{3}{W}{U}: … becomes a 4/4 white and blue
    /// Elemental creature with flying and vigilance until end of turn …".
    /// Animate it and put it on the battlefield, unsummoning-sick, untapped.
    /// </summary>
    private Land AnimatedColonnade(ContinuousEffectsService effects, Player owner)
    {
        var land = CelestialColonnadeFactory.Create(owner, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;
        land.Controller = owner;
        land.HasSummoningSickness = false;
        owner.Zones.Battlefield.AddCard(land);
        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(Majik.Core.Abilities.ActivatedAbility))
            .Cast<Majik.Core.Abilities.ActivatedAbility>()
            .Single();
        animate.Resolve();
        return land;
    }

    [Fact]
    public void CombatValidator_CanAttack_AnimatedLand_IsEligible()
    {
        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects, _alice);

        var validator = new CombatValidator();
        validator.CanAttack(land, _alice).Should().BeTrue(
            "an animated land is effectively a creature (CR 613.1c) and may attack");
    }

    [Fact]
    public void CombatValidator_CanAttack_NonAnimatedLand_IsNotEligible()
    {
        var effects = new ContinuousEffectsService();
        var land = CelestialColonnadeFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);
        land.ActiveEffects = effects;
        land.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(land);

        var validator = new CombatValidator();
        validator.CanAttack(land, _alice).Should().BeFalse(
            "a plain land is not effectively a creature and can't attack");
    }

    [Fact]
    public async Task AnimatedLand_AttacksUnblocked_DealsCombatDamageToPlayer()
    {
        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects, _alice); // 4/4

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(land, _bob),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new Permanent[] { land }, Array.Empty<Permanent>(), ctx);

        _bob.LifeTotal.Should().Be(16, "4 power animated land dealt 4 to Bob");
    }

    [Fact]
    public async Task AnimatedLand_AttacksDeclared_FiresCreatureAttacksEvent()
    {
        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects, _alice);

        Permanent? sawAttacker = null;
        _bus.Subscribe<CreatureAttacksEvent>(e => sawAttacker = e.Attacker);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(land, _bob),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new Permanent[] { land }, Array.Empty<Permanent>(), ctx);

        sawAttacker.Should().BeSameAs(land,
            "the per-attacker CR 508.1f event must name the animated land itself");
    }

    [Fact]
    public async Task AnimatedLand_AttacksBlocked_TradesDamageAndDiesToSba()
    {
        var effects = new ContinuousEffectsService();
        var land = AnimatedColonnade(effects, _alice); // 4/4

        var blocker = NewCreature("Wall", 5, 5, _bob);
        blocker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(blocker);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(land, _bob),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(new BlockPlan(new[]
        {
            new Majik.Core.Players.Agents.BlockerDeclaration(blocker, land),
        }));

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new Permanent[] { land }, new Permanent[] { blocker }, ctx);

        // The 5/5 blocker dealt 5 to the 4/4 land — lethal. It dies as a
        // creature via the lifted Permanent-level lethal-damage SBA (CR 704.5f).
        land.Zone.Should().Be(ZoneType.Graveyard, "5 >= 4 toughness — lethal");
        // The land dealt 4 to the 5/5 blocker — not lethal.
        blocker.Zone.Should().Be(ZoneType.Battlefield, "4 < 5 toughness — survives");
        _bob.LifeTotal.Should().Be(20, "blocked attacker dealt no damage to Bob");
    }

    [Fact]
    public async Task RealCreature_StillAttacks_Identically_Regression()
    {
        var bear = NewCreature("Bear", 2, 2, _alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(bear);

        var flow = new CombatFlow(_bus, _sba);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(bear, _bob),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, StepStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new Permanent[] { bear }, Array.Empty<Permanent>(), ctx);

        _bob.LifeTotal.Should().Be(18, "ordinary creature combat unchanged");
        bear.IsTapped.Should().BeTrue("attacked without vigilance");
    }
}
