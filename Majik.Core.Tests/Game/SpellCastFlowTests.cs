using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Rule 601 spell-casting steps run via agent prompts in order:
/// modes → X → targets → mana → push to stack.
/// </summary>
public class SpellCastFlowTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public async Task VanillaSpell_NoPrompts_LandsOnStack_FiresSpellCastEvent()
    {
        var bolt = new Instant("Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        SpellCastEvent? cast = null;
        _bus.Subscribe<SpellCastEvent>(e => cast = e);

        var spell = await _flow.CastAsync(_alice, bolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, NewContext());

        bolt.Zone.Should().Be(ZoneType.Stack);
        _stack.Count.Should().Be(1);
        _stack.Top.Should().BeSameAs(spell);
        cast.Should().NotBeNull();
    }

    [Fact]
    public async Task TargetedSpell_PromptsForTargets_InOrder_AttachesTargets()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var bolt = new Instant("Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bear });
        agent.QueueMana(ManaPayment.Empty);

        var capturedTargets = new List<object>();
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("creature", 1, 1, new[] { (object)bear }),
            },
            EffectFactory: p =>
            {
                capturedTargets.AddRange(p.Targets.SelectMany(t => t));
                return Array.Empty<IEffect>();
            });

        var spell = await _flow.CastAsync(_alice, bolt, def, agent, NewContext());

        capturedTargets.Should().ContainSingle().Which.Should().BeSameAs(bear);
        spell.Should().NotBeNull();
    }

    [Fact]
    public async Task XSpell_PromptsForX_PassesValueToEffectFactory()
    {
        var fireball = new Instant("Fireball", "X{R}") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueX(3);
        agent.QueueMana(ManaPayment.Empty);

        int? capturedX = null;
        var def = new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p => { capturedX = p.X; return Array.Empty<IEffect>(); });

        await _flow.CastAsync(_alice, fireball, def, agent, NewContext());

        capturedX.Should().Be(3);
    }

    [Fact]
    public async Task ModalSpell_PromptsForMode_PassesIndexToEffectFactory()
    {
        var dual = new Instant("Dual", "1U") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMode(1);
        agent.QueueMana(ManaPayment.Empty);

        int? capturedMode = null;
        var def = new SpellDefinition(
            Modes: new[] { "draw 2", "counter target spell" },
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p => { capturedMode = p.ModeIndex; return Array.Empty<IEffect>(); });

        await _flow.CastAsync(_alice, dual, def, agent, NewContext());

        capturedMode.Should().Be(1);
    }

    [Fact]
    public async Task CastAsync_ForwardsAllPlayersIntoChosenParams()
    {
        var spell = new Instant("AOE", "1B") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        IReadOnlyList<Player>? capturedAllPlayers = null;
        var def = SpellDefinition.Vanilla(p =>
        {
            capturedAllPlayers = p.AllPlayers;
            return Array.Empty<IEffect>();
        });

        await _flow.CastAsync(_alice, spell, def, agent, NewContext());

        capturedAllPlayers.Should().NotBeNull();
        capturedAllPlayers!.Should().BeEquivalentTo(new[] { _alice, _bob });
    }

    [Fact]
    public async Task PreChosenMana_SkipsAgentPrompt_NoDoubleSourcePick()
    {
        // Regression: TurnDriver.DispatchCast prompts the agent for mana
        // sources before invoking SpellCastFlow.CastAsync. CastAsync used
        // to unconditionally prompt again — the player would see two
        // mana selection prompts per cast. When the caller supplies
        // preChosenMana the prompt is skipped.
        var bolt = new Instant("Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new CountingManaAgent();

        await _flow.CastAsync(
            _alice, bolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, NewContext(),
            preChosenMana: ManaPayment.Empty);

        agent.ManaPromptCount.Should().Be(0,
            "preChosenMana supplied — SpellCastFlow must not re-prompt");
    }

    [Fact]
    public async Task NoPreChosenMana_PromptsAgentExactlyOnce()
    {
        // Mirror of the above: when no payment is forwarded, SpellCastFlow
        // remains the canonical caster and prompts once (CR 601.2g).
        var bolt = new Instant("Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new CountingManaAgent();

        await _flow.CastAsync(
            _alice, bolt,
            SpellDefinition.Vanilla(_ => Array.Empty<IEffect>()),
            agent, NewContext());

        agent.ManaPromptCount.Should().Be(1,
            "no preChosenMana — SpellCastFlow prompts exactly once");
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.Main, _stack);

    /// <summary>
    /// Test agent that tallies how many times ChooseManaSourcesAsync is
    /// invoked. All other prompts return defaults; ManaPayment.Empty is
    /// returned for mana so SpellCastFlow's payment metadata is well-formed.
    /// </summary>
    private sealed class CountingManaAgent : IPlayerAgent
    {
        public int ManaPromptCount { get; private set; }

        public Task<Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
        {
            ManaPromptCount++;
            return Task.FromResult(Majik.Core.Players.Agents.ManaPayment.Empty);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine);
        public Task<Majik.Core.Players.Agents.CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(Majik.Core.Players.Agents.CombatPlan.None);
        public Task<Majik.Core.Players.Agents.BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(Majik.Core.Players.Agents.BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }
}
