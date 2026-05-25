using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Keywords;
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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 117.7 / CR 601.2f — end-to-end cast-time wiring of the three-arg
/// <see cref="CostReduction.GetEffectiveCost(ICard, Player, IEnumerable{Player}?)"/>
/// overload through <see cref="SpellCastFlow.CastAsync"/>. Prior to this fix
/// the cast path called the two-arg overload, so battlefield
/// <see cref="SpellCostIncreaseAbility"/> riders (Sphere of Resistance,
/// Trinisphere, Thalia, Damping Sphere, …) were silently skipped at
/// payment-prompt time even though their per-card unit tests asserted the
/// correct increase. These tests pin the live mana-payment surface so the
/// cost the agent is asked to cover already includes every rider on every
/// player's battlefield that matches the spell.
/// </summary>
public class SpellCastFlowCostIncreaseRiderTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowCostIncreaseRiderTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new ZoneService(_bus), _bus);
    }

    /// <summary>
    /// Agent that snapshots the <see cref="ManaCost"/> handed to
    /// <see cref="IPlayerAgent.ChooseManaSourcesAsync"/>. SpellCastFlow's
    /// printed-cost branch passes the post-reduction / post-rider cost in;
    /// asserting against the captured value is how we confirm the three-arg
    /// overload actually fired. Wraps a <see cref="ScriptedAgent"/> by
    /// composition (ScriptedAgent is sealed) and forwards every other
    /// prompt unchanged.
    /// </summary>
    private sealed class CapturingAgent : IPlayerAgent
    {
        private readonly ScriptedAgent _inner = new();
        public ManaCost? CapturedManaCost { get; private set; }

        public void QueueMana(ManaPayment payment) => _inner.QueueMana(payment);

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => _inner.ChoosePriorityActionAsync(ctx, ct);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => _inner.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => _inner.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => _inner.ChooseTargetsAsync(ctx, request, ct);
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => _inner.ChooseXAsync(ctx, source, ct);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => _inner.ChooseModeAsync(ctx, modes, modeIntents, ct);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => _inner.OrderTriggersAsync(ctx, mine, ct);
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        {
            CapturedManaCost = cost;
            return _inner.ChooseManaSourcesAsync(ctx, cost, ct);
        }
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => _inner.DeclareAttackersAsync(ctx, eligibleAttackers, ct);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => _inner.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct);
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseScryDecisionAsync(ctx, peeked, ct);
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _inner.ChooseSurveilDecisionAsync(ctx, peeked, ct);
    }

    private static SpellCostIncreaseAbility SphereOfResistanceRider() =>
        new(
            _ => true,
            (_, _) => 1,
            "Spells cost {1} more to cast.");

    private static SpellCostIncreaseAbility TrinisphereRider() =>
        // CR 117.7 — printed text is "if the total cost to cast a spell is
        // less than three, it costs {3} to cast instead". For wiring
        // purposes the rider only needs to demonstrate the cast path picks
        // it up; this stand-in raises any spell with CMC < 3 to a flat {3}.
        new(
            _ => true,
            (card, caster) =>
            {
                var baseline = CostReduction.GetEffectiveCost(card, caster, allPlayers: null);
                return Math.Max(0, 3 - baseline.TotalValue);
            },
            "Trinisphere (stub): spells cost at least {3}.");

    /// <summary>
    /// Anchors a permanent carrying a SpellCostIncreaseAbility onto the
    /// given player's battlefield. The permanent is a vanilla artifact
    /// shell — only the rider matters at cost-calc time.
    /// </summary>
    private static void AnchorRider(Player onto, SpellCostIncreaseAbility rider, string name)
    {
        var perm = new Artifact(name, "{2}") { Owner = onto, Controller = onto, Zone = ZoneType.Battlefield };
        perm.AddAbility(rider);
        onto.Zones.Battlefield.AddCard(perm);
    }

    // ------------------------------------------------------------------
    // Sphere of Resistance — "Spells cost {1} more to cast."
    // ------------------------------------------------------------------

    [Fact]
    public async Task LightningBolt_PaysPlusOne_WhenOpponentControlsSphereOfResistance()
    {
        AnchorRider(_bob, SphereOfResistanceRider(), "Sphere of Resistance");

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(bolt);

        var agent = new CapturingAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, bolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        agent.CapturedManaCost.Should().NotBeNull(
            "SpellCastFlow must hand the post-rider cost to the agent");
        agent.CapturedManaCost!.Generic.Should().Be(1,
            "Sphere of Resistance adds {1} generic (CR 117.7)");
        agent.CapturedManaCost.Red.Should().Be(1,
            "coloured pips are untouched (CR 117.7c)");
        agent.CapturedManaCost.TotalValue.Should().Be(2,
            "Bolt under Sphere = {1}{R}");
    }

    [Fact]
    public async Task BlueSpell_PaysGenericThree_WhenOpponentControlsTrinisphereStub()
    {
        AnchorRider(_bob, TrinisphereRider(), "Trinisphere");

        // A {U} instant — baseline CMC 1; Trinisphere stub should add {2}
        // generic so the effective cost is {2}{U} = 3.
        var ponder = new Instant("Ponder", "{U}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(ponder);

        var agent = new CapturingAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, ponder,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        agent.CapturedManaCost.Should().NotBeNull();
        agent.CapturedManaCost!.Generic.Should().Be(2,
            "Trinisphere lifts a 1-CMC spell to a total of 3 (added {2} generic)");
        agent.CapturedManaCost.Blue.Should().Be(1, "coloured pip preserved");
        agent.CapturedManaCost.TotalValue.Should().Be(3);
    }

    // ------------------------------------------------------------------
    // Thalia, Guardian of Thraben — "Noncreature spells cost {1} more."
    // Predicate is restricted, so we cover BOTH the creature-spell (no
    // tax) AND the noncreature-spell (+{1}) branches.
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreatureSpell_PaysPrintedCost_WhenOpponentControlsThalia()
    {
        var thalia = ThaliaGuardianOfThrabenFactory.Create(_bob);
        _bob.Zones.Battlefield.AddCard(thalia);
        thalia.SetZone(ZoneType.Battlefield);

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2) { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(goblin);

        var agent = new CapturingAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, goblin,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        agent.CapturedManaCost.Should().NotBeNull();
        agent.CapturedManaCost!.Generic.Should().Be(0,
            "Thalia's rider only taxes noncreature spells; creature spells unchanged");
        agent.CapturedManaCost.Red.Should().Be(1);
        agent.CapturedManaCost.TotalValue.Should().Be(1);
    }

    [Fact]
    public async Task LightningBolt_PaysPlusOne_WhenOpponentControlsThalia()
    {
        var thalia = ThaliaGuardianOfThrabenFactory.Create(_bob);
        _bob.Zones.Battlefield.AddCard(thalia);
        thalia.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(bolt);

        var agent = new CapturingAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, bolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        agent.CapturedManaCost.Should().NotBeNull();
        agent.CapturedManaCost!.Generic.Should().Be(1,
            "Thalia's rider matches noncreature spells (Instant) → +{1}");
        agent.CapturedManaCost.Red.Should().Be(1);
        agent.CapturedManaCost.TotalValue.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // No rider in play → printed cost passes through untouched. Pins the
    // overload's null-allPlayers fallback shape (regression guard for a
    // future refactor that accidentally drops the parameter entirely).
    // ------------------------------------------------------------------

    [Fact]
    public async Task LightningBolt_PaysPrintedCost_WhenNoRiderInPlay()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(bolt);

        var agent = new CapturingAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

        await _flow.CastAsync(
            _alice, bolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, ctx);

        agent.CapturedManaCost.Should().NotBeNull();
        agent.CapturedManaCost!.Generic.Should().Be(0);
        agent.CapturedManaCost.Red.Should().Be(1);
        agent.CapturedManaCost.TotalValue.Should().Be(1, "printed {R} only when no rider is in play");
    }
}
