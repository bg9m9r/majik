using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Combat;
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

public class PreventDamageTests
{
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;
    private readonly StateBasedActions _sba;
    private readonly ReplacementBus _replacements = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public PreventDamageTests()
    {
        _zones = new ZoneService(_bus);
        _sba = new StateBasedActions(_bus, _zones);
    }

    [Fact]
    public async Task PreventNextN_AbsorbsDamage_DecrementsShield()
    {
        var bear = NewCreature("Bear", 3, 3, _alice);
        // Bob has a "prevent next 2 damage to you" shield.
        var shield = new PreventNextNDamageShield(_bob, amount: 2);
        _replacements.Register<DamageIntent>(shield);

        await RunUnblockedAttack(bear);

        _bob.LifeTotal.Should().Be(19); // 3 dmg - 2 prevented
        shield.Remaining.Should().Be(0);
    }

    [Fact]
    public async Task PreventNextN_AbsorbsFully_NoLifeLoss()
    {
        var bear = NewCreature("Bear", 2, 2, _alice);
        var shield = new PreventNextNDamageShield(_bob, amount: 5);
        _replacements.Register<DamageIntent>(shield);

        await RunUnblockedAttack(bear);

        _bob.LifeTotal.Should().Be(20);
        shield.Remaining.Should().Be(3);
    }

    private async Task RunUnblockedAttack(Creature attacker)
    {
        attacker.SetZone(ZoneType.Battlefield);
        attacker.HasSummoningSickness = false;
        _alice.Zones.Battlefield.AddCard(attacker);

        var flow = new CombatFlow(_bus, _sba, _replacements);
        var atk = new ScriptedAgent();
        atk.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(attacker, _bob),
        }));
        var blk = new ScriptedAgent();
        blk.QueueBlockers(BlockPlan.None);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        await flow.RunCombatAsync(_alice, _bob, atk, blk,
            new[] { attacker }, Array.Empty<Creature>(), ctx);
    }

    private static Creature NewCreature(string name, int p, int t, Player owner) =>
        new(name, "1", p, t) { Owner = owner, Controller = owner };

    /// <summary>"Prevent the next N damage to PLAYER" shield (CR 615.1).</summary>
    private sealed class PreventNextNDamageShield : IReplacementEffect<DamageIntent>
    {
        private readonly Player _target;
        public int Remaining { get; private set; }

        public PreventNextNDamageShield(Player target, int amount)
        {
            _target = target;
            Remaining = amount;
        }

        public bool OneShot => false;
        public object? Tag => this;  // self-tag → fires once per intent

        public bool Applies(DamageIntent intent, IReadOnlyList<object> history) =>
            Remaining > 0 && ReferenceEquals(intent.TargetPlayer, _target) && intent.Amount > 0;

        public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history)
        {
            var absorbed = Math.Min(Remaining, intent.Amount);
            Remaining -= absorbed;
            return intent with { Amount = intent.Amount - absorbed };
        }
    }
}
