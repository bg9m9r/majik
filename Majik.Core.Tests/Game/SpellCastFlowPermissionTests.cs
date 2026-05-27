using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

public class SpellCastFlowPermissionTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowPermissionTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    [Fact]
    public async Task Sorcery_OnOpponentTurn_Throws()
    {
        var sorc = new Sorcery("Divination", "2U") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(sorc);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            activePlayer: _bob, 1, PhaseStateType.PreCombatMain, _stack);
        var agent = new ScriptedAgent();

        var act = async () => await _flow.CastAsync(_alice, sorc,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*your turn*");
    }

    [Fact]
    public async Task Instant_OnOpponentTurn_OK()
    {
        var bolt = new Instant("Bolt", "R") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(bolt);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            activePlayer: _bob, 1, PhaseStateType.End, _stack);
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(_alice, bolt,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()),
            agent, ctx);

        _stack.Count.Should().Be(1);
    }

    [Fact]
    public async Task XSpell_PromptsForX_PaysGenericOnTop()
    {
        var fireball = new Instant("Fireball", "R") { Owner = _alice, Zone = ZoneType.Hand };
        _alice.Zones.Hand.AddCard(fireball);

        Majik.Core.ValueObjects.ManaCost? promptedCost = null;
        var agent = new InspectingAgent();
        agent.X = 3;
        agent.ManaCallback = c => promptedCost = c;

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

        await _flow.CastAsync(_alice, fireball,
            new SpellDefinition(
                Modes: System.Array.Empty<string>(),
                HasVariableX: true,
                TargetRequests: System.Array.Empty<TargetRequest>(),
                EffectFactory: _ => System.Array.Empty<IEffect>()),
            agent, ctx);

        promptedCost.Should().NotBeNull();
        promptedCost!.Generic.Should().Be(3);
        promptedCost.Red.Should().Be(1);
    }

    private sealed class InspectingAgent : IPlayerAgent
    {
        public int X { get; set; }
        public System.Action<Majik.Core.ValueObjects.ManaCost>? ManaCallback { get; set; }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext c, CancellationToken ct = default) => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext c, IReadOnlyList<ICard> h, int n, CancellationToken ct = default) => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext c, IReadOnlyList<ICard> h, int n, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ICard>>(System.Array.Empty<ICard>());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext c, TargetRequest r, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<object>>(System.Array.Empty<object>());
        public Task<int> ChooseXAsync(GameContext c, ICard s, CancellationToken ct = default) => Task.FromResult(X);
        public Task<int> ChooseModeAsync(GameContext c, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext c, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => Task.FromResult(mine);
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext c, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
        { ManaCallback?.Invoke(cost); return Task.FromResult(ManaPayment.Empty); }
        public Task<CombatPlan> DeclareAttackersAsync(GameContext c, IReadOnlyList<Creature> e, CancellationToken ct = default) => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext c, IReadOnlyList<Creature> a, IReadOnlyList<Creature> e, CancellationToken ct = default) => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: System.Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: System.Array.Empty<ICard>()));
    }
}
