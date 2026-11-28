using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 700.2 / 601.2c — a targeted mode with too few legal targets can't be
/// chosen. The cast flow computes that per mode from the same pool and minimum
/// target collection uses, and hands it to the agent with the mode prompt.
/// </summary>
public class ModeAvailabilityTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ModeAvailabilityTests() => _stack = new Majik.Core.Stack.Stack(_bus);

    private GameContext Ctx => new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private SpellDefinition Charm(bool sparse = false)
    {
        var artifact = new TargetRequest("target artifact", 0, 1, Array.Empty<object>(),
            CandidateGatherer: ctx => ctx.AllPlayers.SelectMany(p => p.Zones.Battlefield.GetCards())
                .Where(c => c is Artifact).Cast<object>().ToList(),
            PrintedMinTargets: 1, ModeIndex: sparse ? 0 : null);
        var player = new TargetRequest("target player", 0, 1, Array.Empty<object>(),
            CandidateGatherer: ctx => ctx.AllPlayers.Cast<object>().ToList(),
            PrintedMinTargets: 1, ModeIndex: sparse ? 2 : null);
        return new SpellDefinition(
            Modes: sparse
                ? new[] { "Destroy target artifact.", "Draw a card.", "Target player discards a card." }
                : new[] { "Destroy target artifact.", "Target player discards a card." },
            HasVariableX: false,
            TargetRequests: new[] { artifact, player },
            EffectFactory: _ => Array.Empty<IEffect>());
    }

    [Fact]
    public void AModeWithNoLegalTarget_IsUnavailable_WithAReason()
    {
        var reasons = SpellCastFlow.ModeUnavailableReasons(Charm(), Ctx, synthesize: false);

        reasons.Should().HaveCount(2);
        reasons[0].Should().Be("No legal target: target artifact.");
        reasons[1].Should().BeNull();
    }

    [Fact]
    public void OnceATargetExists_TheModeIsAvailable()
    {
        var relic = new Artifact("Relic", "{1}");
        relic.SetOwner(_bob); relic.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(relic);

        SpellCastFlow.ModeUnavailableReasons(Charm(), Ctx, synthesize: false)
            .Should().AllSatisfy(r => r.Should().BeNull());
    }

    [Fact]
    public void SparseModeIndexedRequests_MapToTheirOwnModes()
    {
        var reasons = SpellCastFlow.ModeUnavailableReasons(Charm(sparse: true), Ctx, synthesize: false);

        reasons.Should().HaveCount(3);
        reasons[0].Should().NotBeNull();
        reasons[1].Should().BeNull("an untargeted mode is always available");
        reasons[2].Should().BeNull();
    }

    [Fact]
    public async Task TheCastFlow_HandsTheAgentTheReasons_WithTheModePrompt()
    {
        var card = new Instant("Test Charm", "{0}");
        card.SetOwner(_alice); card.SetController(_alice);
        _alice.Zones.Hand.AddCard(card); card.SetZone(ZoneType.Hand);
        var agent = new ModeProbe(_bob);
        var flow = new SpellCastFlow(_stack, new Majik.Core.Services.ZoneService(_bus), _bus);

        await flow.CastAsync(_alice, card, Charm(), agent, Ctx);

        agent.Reasons.Should().NotBeNull();
        agent.Reasons![0].Should().NotBeNull();
        agent.Reasons[1].Should().BeNull();
    }

    private sealed class ModeProbe(object target) : DelegatingAgent, IPlayerAgent
    {
        public IReadOnlyList<string?>? Reasons { get; private set; }

        public Task<int> ChooseModeAmongAsync(GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<string?> unavailableReasons, IReadOnlyList<BotIntent>? modeIntents = null,
            CancellationToken ct = default)
        {
            Reasons = unavailableReasons;
            return Task.FromResult(1);
        }

        public override Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(new[] { target });

        public override Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);
    }
}