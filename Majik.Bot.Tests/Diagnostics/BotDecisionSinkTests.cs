using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
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

    // ------------------------------------------------------------------
    // CombatPolicy.PickBlockers — "Combat.Blockers"
    // ------------------------------------------------------------------

    [Fact]
    public void CombatPolicy_PickBlockers_EmitsCombatBlockersDecision()
    {
        var s = new BotTestScenario();
        var attacker = s.AddCreatureToBattlefield(s.Opponent, "Goblin", 2, 1);
        var blocker = s.AddCreatureToBattlefield(s.Self, "Wall", 0, 4);
        s.AddCreatureToBattlefield(s.Self, "Bear", 2, 2);

        var sink = new CapturingBotDecisionSink();
        var policy = new CombatPolicy(ArchetypeWeights.Burn, sink: sink);
        var plan = policy.PickBlockers(
            s.Context, s.Self,
            new[] { attacker },
            new[] { blocker, s.Self.Zones.Battlefield.GetCards().OfType<Creature>().First(c => c.Name == "Bear") });

        plan.Blockers.Should().NotBeEmpty();
        var d = sink.OfType("Combat.Blockers").Single();
        d.Chosen.Should().StartWith("Block:");
        d.Context.Should().ContainKey("eligibleBlockers");
        d.Context["eligibleBlockers"].Should().Be("2");
        d.Context.Should().ContainKey("attackerCount");
    }

    [Fact]
    public void CombatPolicy_PickBlockers_NoBlocksAvailable_FlagsTakeFullDamage()
    {
        var s = new BotTestScenario();
        var attacker = s.AddCreatureToBattlefield(s.Opponent, "Giant", 5, 5);
        // Blocker too small to hard-block; the v1 picker only assigns
        // when blocker.Toughness > attacker.Power, so no blocks happen.
        var blocker = s.AddCreatureToBattlefield(s.Self, "Squire", 1, 1);

        var sink = new CapturingBotDecisionSink();
        var policy = new CombatPolicy(ArchetypeWeights.Burn, sink: sink);
        policy.PickBlockers(s.Context, s.Self, new[] { attacker }, new[] { blocker });

        var d = sink.OfType("Combat.Blockers").Single();
        d.Chosen.Should().Be("Block:{}");
        d.Context.Should().ContainKey("takeFullDamage");
        d.Context["takeFullDamage"].Should().Be("true");
    }

    [Fact]
    public void CombatPolicy_PickBlockers_NoSink_StillReturnsSamePlan()
    {
        // Sink is observational; with or without it the v1 picker returns
        // the same BlockPlan.
        var s = new BotTestScenario();
        var attacker = s.AddCreatureToBattlefield(s.Opponent, "Goblin", 2, 1);
        var blocker = s.AddCreatureToBattlefield(s.Self, "Wall", 0, 4);

        var noSink = new CombatPolicy(ArchetypeWeights.Burn);
        var sinkPol = new CombatPolicy(ArchetypeWeights.Burn, sink: new CapturingBotDecisionSink());
        noSink.PickBlockers(s.Context, s.Self, new[] { attacker }, new[] { blocker })
              .Blockers.Count
            .Should().Be(
                sinkPol.PickBlockers(s.Context, s.Self, new[] { attacker }, new[] { blocker })
                       .Blockers.Count);
    }

    // ------------------------------------------------------------------
    // TriggerOrderPolicy.Order — "TriggerOrder"
    // ------------------------------------------------------------------

    [Fact]
    public void TriggerOrderPolicy_Order_TwoTriggers_EmitsTriggerOrderDecision()
    {
        var s = new BotTestScenario();
        // Source cards just need a printed name; the score uses card.Name.
        var damageSrc = new Creature("Lightning Hound", "", 1, 1);
        var lifegainSrc = new Creature("Soul Warden", "", 1, 1);
        damageSrc.ChangeOwner(s.Self);
        lifegainSrc.ChangeOwner(s.Self);

        // Use the simplest factory available — OnEnterBattlefieldSelf is a
        // pure construction helper that doesn't touch event-bus state.
        var dmg = new TriggeredAbility(
            damageSrc, s.Self,
            Triggers.OnEnterBattlefieldSelf(damageSrc),
            effects: new IEffect[] { new Effect("deal 3 damage to target", () => { }) });
        var heal = new TriggeredAbility(
            lifegainSrc, s.Self,
            Triggers.OnEnterBattlefieldSelf(lifegainSrc),
            effects: new IEffect[] { new Effect("gain 1 life", () => { }) });

        var sink = new CapturingBotDecisionSink();
        var ordered = TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { heal, dmg }, sink);
        ordered.Should().HaveCount(2);

        var d = sink.OfType("TriggerOrder").Single();
        d.Chosen.Should().StartWith("Trigger:");
        d.Context.Should().ContainKey("triggerCount");
        d.Context["triggerCount"].Should().Be("2");
        d.Alternatives.Should().NotBeEmpty();
    }

    [Fact]
    public void TriggerOrderPolicy_Order_SingleTrigger_DoesNotEmit()
    {
        // Single-trigger case short-circuits — no decision to make, no
        // signal worth logging.
        var s = new BotTestScenario();
        var src = new Creature("X", "", 1, 1);
        src.ChangeOwner(s.Self);
        var trig = new TriggeredAbility(src, s.Self, Triggers.OnEnterBattlefieldSelf(src));

        var sink = new CapturingBotDecisionSink();
        TriggerOrderPolicy.Order(s.Context, new ITriggeredAbility[] { trig }, sink);

        sink.OfType("TriggerOrder").Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // ModalPolicy.PickMode — "Mode"
    // ------------------------------------------------------------------

    [Fact]
    public void ModalPolicy_PickMode_EmitsModeDecision_WithAlternatives()
    {
        var s = new BotTestScenario();
        var modes = new[]
        {
            "Destroy target creature.",            // high-impact
            "You gain 1 life.",                    // low-impact
            "Target player loses 1 life. You lose 2 life.",  // drawback
        };

        var sink = new CapturingBotDecisionSink();
        var idx = ModalPolicy.PickMode(s.Context, s.Self, modes, sink);
        idx.Should().Be(0, "destroy outscores gain-1-life and the self-cost line");

        var d = sink.OfType("Mode").Single();
        d.Chosen.Should().StartWith("Mode[0]:");
        d.Context.Should().ContainKey("modeCount");
        d.Context["modeCount"].Should().Be("3");
        d.Alternatives.Should().HaveCount(2);
        // Alternatives ranked by score, descending.
        d.Alternatives[0].Score.Should().BeGreaterThanOrEqualTo(d.Alternatives[1].Score);
    }

    [Fact]
    public void ModalPolicy_PickMode_SingleMode_FlagsForced()
    {
        var s = new BotTestScenario();
        var sink = new CapturingBotDecisionSink();
        ModalPolicy.PickMode(s.Context, s.Self, new[] { "Draw a card." }, sink);

        var d = sink.OfType("Mode").Single();
        d.Context.Should().ContainKey("forced");
        d.Context["forced"].Should().Be("true");
        d.Alternatives.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // TargetPolicy.Pick — "Target"
    // ------------------------------------------------------------------

    [Fact]
    public void TargetPolicy_Pick_EmitsTargetDecision_WithAlternatives()
    {
        var s = new BotTestScenario();
        var big = s.AddCreatureToBattlefield(s.Opponent, "Dragon", 5, 5);
        var small = s.AddCreatureToBattlefield(s.Opponent, "Goblin", 1, 1);
        var ours = s.AddCreatureToBattlefield(s.Self, "OurBear", 2, 2);

        var request = new TargetRequest(
            Description: "Target creature an opponent controls",
            MinTargets: 1, MaxTargets: 1,
            LegalCandidates: new object[] { big, small, ours });

        var sink = new CapturingBotDecisionSink();
        var picked = TargetPolicy.Pick(s.Context, s.Self, request, sink);
        picked.Should().HaveCount(1);
        picked[0].Should().BeSameAs(big, "the highest-power opposing creature is the best target");

        var d = sink.OfType("Target").Single();
        d.Chosen.Should().Contain("Dragon");
        d.Context.Should().ContainKey("candidateCount");
        d.Context["candidateCount"].Should().Be("3");
        d.Context.Should().ContainKey("maxTargets");
        d.Alternatives.Should().NotBeEmpty();
    }

    [Fact]
    public void TargetPolicy_Pick_NoCandidates_DoesNotEmit()
    {
        // Empty candidate set short-circuits before scoring — nothing to log.
        var s = new BotTestScenario();
        var request = new TargetRequest(
            Description: "Any target",
            MinTargets: 0, MaxTargets: 1,
            LegalCandidates: Array.Empty<object>());

        var sink = new CapturingBotDecisionSink();
        TargetPolicy.Pick(s.Context, s.Self, request, sink);

        sink.OfType("Target").Should().BeEmpty();
    }
}
