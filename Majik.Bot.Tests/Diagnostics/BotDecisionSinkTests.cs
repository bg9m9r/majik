using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Diagnostics;

public class BotDecisionSinkTests
{
    [Fact]
    public void PriorityPolicy_EmitsPriorityDecision_WithChosenAndAlternatives()
    {
        var s = new BotTestScenario();
        s.AddLandToBattlefield(s.Self, "Mountain1");
        s.AddLandToBattlefield(s.Self, "Mountain2");
        s.AddCardToHand(s.Self, new Creature("Goblin Guide", "{R}", 2, 2));
        // Also offer a land in hand so multiple candidates compete.
        s.AddCardToHand(s.Self, new Land("Mountain3"));

        var sink = new CapturingBotDecisionSink();
        var pol = new PriorityPolicy(ArchetypeWeights.Burn, sink);

        var action = pol.Pick(s.Context, s.Self);
        action.Should().NotBeNull();

        sink.Decisions.Should().HaveCount(1);
        var d = sink.Decisions[0];
        d.DecisionType.Should().Be("Priority");
        d.Chosen.Should().NotBeNullOrWhiteSpace();
        // Pass always shows up alongside any non-Pass winner.
        d.Alternatives.Should().NotBeEmpty();
        d.Context.Should().ContainKey("turn");
        d.Context.Should().ContainKey("phase");
        d.Context.Should().ContainKey("manaAvailable");
    }

    [Fact]
    public void PriorityPolicy_NoSink_StillPicksTheSameAction()
    {
        // Verifies the sink is purely observational — wiring it (or not)
        // must not change which action the policy returns.
        var s = new BotTestScenario();
        s.AddCardToHand(s.Self, new Land("Forest"));

        var nullSinkPol = new PriorityPolicy(ArchetypeWeights.Burn);
        var sinkPol = new PriorityPolicy(ArchetypeWeights.Burn, new CapturingBotDecisionSink());

        nullSinkPol.Pick(s.Context, s.Self).GetType()
            .Should().Be(sinkPol.Pick(s.Context, s.Self).GetType());
    }

    [Fact]
    public void PriorityPolicy_ManaScrewFlag_SetWhenNoLandsButNonLandInHand()
    {
        var s = new BotTestScenario();
        s.AddCardToHand(s.Self, new Creature("Goblin", "{R}", 1, 1));

        var sink = new CapturingBotDecisionSink();
        var pol = new PriorityPolicy(ArchetypeWeights.Burn, sink);
        pol.Pick(s.Context, s.Self);

        sink.Decisions.Should().HaveCount(1);
        sink.Decisions[0].Context.Should().ContainKey("manaScrew");
        sink.Decisions[0].Context["manaScrew"].Should().Be("true");
    }

    [Fact]
    public void CombatSearch_EmitsAttackerDecision_WithSearchMode()
    {
        var s = new BotTestScenario();
        var attacker = s.AddCreatureToBattlefield(s.Self, "Tarmogoyf", 3, 3);
        s.AddCreatureToBattlefield(s.Opponent, "Bear", 2, 2);

        var sink = new CapturingBotDecisionSink();
        var policy = new CombatPolicy(ArchetypeWeights.Prowess, sink: sink);
        var plan = policy.PickAttackers(s.Context, s.Self, new Creature[] { attacker });
        plan.Attackers.Should().HaveCount(1);

        var combatDecisions = sink.OfType("Combat.Attackers").ToList();
        combatDecisions.Should().HaveCount(1);
        var d = combatDecisions[0];
        d.Chosen.Should().StartWith("Attack:{");
        d.Context.Should().ContainKey("search");
        d.Context["search"].Should().BeOneOf("greedy", "minimax");
        d.Context.Should().ContainKey("oppBlockers");
        d.Context["oppBlockers"].Should().Be("1");
    }

    [Fact]
    public void CombatSearch_NoBlockers_FlagsOppNoBlockers()
    {
        var s = new BotTestScenario();
        var attacker = s.AddCreatureToBattlefield(s.Self, "Goblin", 2, 1);

        var sink = new CapturingBotDecisionSink();
        var policy = new CombatPolicy(ArchetypeWeights.Burn, sink: sink);
        policy.PickAttackers(s.Context, s.Self, new Creature[] { attacker });

        var d = sink.OfType("Combat.Attackers").Single();
        d.Context.Should().ContainKey("oppNoBlockers");
        d.Context["oppNoBlockers"].Should().Be("true");
    }

    [Fact]
    public async Task HeuristicStrategy_ThreadsSinkFromConfig_IntoPolicies()
    {
        var sink = new CapturingBotDecisionSink();
        var cfg = new BotConfig("Burn", DecisionSink: sink);
        var bot = new BotPlayerAgent(new Majik.Core.Players.Player("BobBot", 20), cfg);

        // Construct a minimal context with the bot's player and run priority.
        var s = new BotTestScenario();
        // Use s.Self as the bot's seat for the call shape — sink should still
        // capture the decision because HeuristicStrategy was built from cfg.
        var action = await bot.ChoosePriorityActionAsync(s.Context);
        action.Should().NotBeNull();

        sink.Decisions.Should().NotBeEmpty();
        sink.Decisions.Should().Contain(d => d.DecisionType == "Priority");
    }

    [Fact]
    public void NullSink_IsNoOp_AndUsedByDefault()
    {
        // The contract: NullBotDecisionSink.Record must not throw, and the
        // bot defaults to it when BotConfig.DecisionSink is null.
        NullBotDecisionSink.Instance.Invoking(s =>
            s.Record(new BotDecision("X", "y", 0, Array.Empty<BotDecisionAlternative>(),
                new Dictionary<string, string>())))
            .Should().NotThrow();

        new BotConfig("Burn").DecisionSink.Should().BeNull();
    }
}
