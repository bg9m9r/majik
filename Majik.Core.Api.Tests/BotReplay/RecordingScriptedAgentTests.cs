using FluentAssertions;
using Majik.Core.Api.BotReplay;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Moq;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Api.Tests.BotReplay;

/// <summary>
/// Record/replay at the <see cref="IPlayerAgent"/> boundary:
/// <see cref="RecordingPlayerAgent"/> decorates the live bot, appending every
/// answer (awaited, ordered, botSeq-monotonic) before returning it verbatim;
/// <see cref="ScriptedPlayerAgent"/> replays the recorded stream against the
/// same prompts, throwing into the replay's graceful stop on kind mismatch /
/// exhaustion, and falls through to a continuation agent at the live edge
/// (the rebuilt game keeps running after the script ends — agents cannot be
/// swapped post-start, so the handoff is composed in).
/// </summary>
public class RecordingScriptedAgentTests
{
    [Fact]
    public async Task Recording_DelegatesVerbatim_AndAppendsMonotonicSeqWithMatchingKinds()
    {
        var (self, opp) = BuildPlayers();
        var ctx = Ctx(self, opp);
        var attacker = SeedCreature(opp, "Raging Goblin", 1, 1);
        var blocker = SeedCreature(self, "Grizzly Bears", 2, 2);
        var expectedPlan = new BlockPlan(new[] { new BlockerDeclaration(blocker, attacker) });

        var inner = new Mock<IPlayerAgent>(MockBehavior.Strict);
        inner.Setup(a => a.ChooseXAsync(ctx, It.IsAny<ICard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        inner.Setup(a => a.ChooseYesNoAsync(ctx, "pay?", "Shockland", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        inner.Setup(a => a.DeclareBlockersAsync(
                ctx, It.IsAny<IReadOnlyList<Permanent>>(), It.IsAny<IReadOnlyList<Creature>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPlan);

        var recorded = new List<BotDecisionRecord>();
        var agent = new RecordingPlayerAgent(inner.Object, r =>
        {
            recorded.Add(r);
            return Task.CompletedTask;
        });

        var x = await agent.ChooseXAsync(ctx, blocker);
        var yes = await agent.ChooseYesNoAsync(ctx, "pay?", "Shockland");
        var plan = await agent.DeclareBlockersAsync(ctx, new[] { attacker }, new[] { blocker });

        // Verbatim delegation.
        x.Should().Be(4);
        yes.Should().BeTrue();
        plan.Should().BeSameAs(expectedPlan);

        // Ordered, monotonic, kind-matched records.
        recorded.Select(r => r.BotSeq).Should().Equal(0, 1, 2);
        recorded.Select(r => r.Kind).Should().Equal(
            BotDecisionKind.X, BotDecisionKind.YesNo, BotDecisionKind.Blockers);
    }

    [Fact]
    public async Task Recording_StartSeq_ContinuesNumberingAfterRehydration()
    {
        var inner = new Mock<IPlayerAgent>();
        inner.Setup(a => a.ChooseXAsync(It.IsAny<GameContext>(), It.IsAny<ICard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var recorded = new List<BotDecisionRecord>();
        var agent = new RecordingPlayerAgent(
            inner.Object, r => { recorded.Add(r); return Task.CompletedTask; }, startSeq: 7);

        var (self, opp) = BuildPlayers();
        await agent.ChooseXAsync(Ctx(self, opp), SeedCreature(self, "Bear", 2, 2));

        recorded.Should().ContainSingle().Which.BotSeq.Should().Be(7);
    }

    [Fact]
    public async Task Recording_AppendIsAwaitedBeforeReturning()
    {
        var inner = new Mock<IPlayerAgent>();
        inner.Setup(a => a.ChooseXAsync(It.IsAny<GameContext>(), It.IsAny<ICard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var appendCompleted = false;
        var gate = new TaskCompletionSource();
        var agent = new RecordingPlayerAgent(inner.Object, async _ =>
        {
            await gate.Task;
            appendCompleted = true;
        });

        var (self, opp) = BuildPlayers();
        var pending = agent.ChooseXAsync(Ctx(self, opp), SeedCreature(self, "Bear", 2, 2));

        pending.IsCompleted.Should().BeFalse("the answer must not be returned before the append lands");
        gate.SetResult();
        await pending;
        appendCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Scripted_AnswersSamePromptsIdentically_ByIds()
    {
        var (self, opp) = BuildPlayers();
        var ctx = Ctx(self, opp);
        var attacker = SeedCreature(opp, "Raging Goblin", 1, 1);
        var blocker = SeedCreature(self, "Grizzly Bears", 2, 2);

        var script = new[]
        {
            new BotDecisionRecord(0, BotDecisionKind.X, BotDecisionCodec.EncodeX(4)),
            new BotDecisionRecord(1, BotDecisionKind.YesNo, BotDecisionCodec.EncodeYesNo(true)),
            new BotDecisionRecord(2, BotDecisionKind.Blockers, BotDecisionCodec.EncodeBlockers(
                new BlockPlan(new[] { new BlockerDeclaration(blocker, attacker) }))),
        };

        var agent = new ScriptedPlayerAgent(script);

        (await agent.ChooseXAsync(ctx, blocker)).Should().Be(4);
        (await agent.ChooseYesNoAsync(ctx, "pay?", "Shockland")).Should().BeTrue();
        var plan = await agent.DeclareBlockersAsync(ctx, new[] { attacker }, new[] { blocker });
        plan.Blockers.Should().ContainSingle();
        plan.Blockers[0].Blocker.InstanceId.Should().Be(blocker.InstanceId);
        plan.Blockers[0].Attacker.InstanceId.Should().Be(attacker.InstanceId);
    }

    [Fact]
    public async Task Scripted_KindMismatch_ThrowsInvalidOperation()
    {
        var (self, opp) = BuildPlayers();
        var script = new[]
        {
            new BotDecisionRecord(0, BotDecisionKind.YesNo, BotDecisionCodec.EncodeYesNo(true)),
        };
        var agent = new ScriptedPlayerAgent(script);

        // The replay prompt asks for X but the stream recorded a YesNo.
        var act = () => agent.ChooseXAsync(Ctx(self, opp), SeedCreature(self, "Bear", 2, 2));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Scripted_ExhaustedWithoutContinuation_ThrowsInvalidOperation()
    {
        var (self, opp) = BuildPlayers();
        var agent = new ScriptedPlayerAgent(Array.Empty<BotDecisionRecord>());

        var act = () => agent.ChooseXAsync(Ctx(self, opp), SeedCreature(self, "Bear", 2, 2));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Scripted_ExhaustedWithContinuation_FallsThroughToLiveAgent()
    {
        var (self, opp) = BuildPlayers();
        var ctx = Ctx(self, opp);

        var live = new Mock<IPlayerAgent>();
        live.Setup(a => a.ChooseXAsync(ctx, It.IsAny<ICard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(9);

        var script = new[]
        {
            new BotDecisionRecord(0, BotDecisionKind.X, BotDecisionCodec.EncodeX(4)),
        };
        var agent = new ScriptedPlayerAgent(script, continuation: live.Object);
        var bear = SeedCreature(self, "Bear", 2, 2);

        (await agent.ChooseXAsync(ctx, bear)).Should().Be(4, "first prompt replays the script");
        (await agent.ChooseXAsync(ctx, bear)).Should().Be(9,
            "past the live edge the continuation agent (recording over the live bot) answers");
        live.Verify(a => a.ChooseXAsync(ctx, It.IsAny<ICard>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recording_UnsupportedEncode_DegradesViaCallback_AnswerStillReturned()
    {
        var (self, opp) = BuildPlayers();
        var ctx = Ctx(self, opp);
        var spell = SeedCreature(self, "Bear", 2, 2);

        var exotic = new Mock<Majik.Core.Costs.IAlternativeCost>();
        exotic.SetupGet(c => c.Description).Returns("exotic");
        exotic.SetupGet(c => c.AlternativeManaCost).Returns(Majik.Core.ValueObjects.ManaCost.Parse("1"));

        var inner = new Mock<IPlayerAgent>();
        inner.Setup(a => a.ChoosePriorityActionAsync(ctx, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PriorityAction.CastSpell(
                spell, Array.Empty<object>(), AlternativeCost: exotic.Object));

        var recorded = new List<BotDecisionRecord>();
        UnsupportedBotDecisionException? observed = null;
        var agent = new RecordingPlayerAgent(
            inner.Object,
            r => { recorded.Add(r); return Task.CompletedTask; },
            onUnsupported: ex => observed = ex);

        var answer = await agent.ChoosePriorityActionAsync(ctx);

        answer.Should().BeOfType<PriorityAction.CastSpell>("the LIVE game must continue unharmed");
        recorded.Should().BeEmpty("an unsupported answer is degraded (skipped), never corrupted");
        observed.Should().NotBeNull();
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    private static (Player Self, Player Opp) BuildPlayers()
        => (new Player("Bob", 20), new Player("Alice", 20));

    private static GameContext Ctx(Player self, Player opp) => new(
        self, new[] { self, opp }, activePlayer: self, turnNumber: 1,
        currentPhase: null, stack: new Majik.Core.Stack.Stack());

    private static Creature SeedCreature(Player p, string name, int power, int toughness)
    {
        var c = new Creature(name, "1", power, toughness) { Owner = p, Controller = p };
        p.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
