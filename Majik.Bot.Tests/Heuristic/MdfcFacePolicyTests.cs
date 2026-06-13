using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Bot.Heuristic;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Heuristic;

/// <summary>
/// CR 712.3 face-choice policy for MDFC casts. Deadlock-killer default: the
/// spell (front) face wins when its cost is affordable from available mana
/// (UntappedManaSources — enumerator-symmetric); otherwise the land (back)
/// face wins while the land drop is available.
/// </summary>
public class MdfcFacePolicyTests
{
    // Sink into Stupor // Soporific Springs shape: front {1}{U}{U} bounce
    // instant, back land. We pass the faces directly so the policy is exercised
    // in isolation from the engine prompt construction.
    private static MdfcFaceChoice Front => new("Sink into Stupor", "{1}{U}{U}", IsBack: false);
    private static MdfcFaceChoice BackLand => new("Soporific Springs", "", IsBack: true);

    [Fact]
    public void ZeroMana_DropAvailable_PicksLandFace()
    {
        var s = new BotTestScenario(); // active player, PreCombatMain, land drop available
        // No lands in play → 0 mana → front {1}{U}{U} unaffordable.

        var chosen = MdfcFacePolicy.Pick(s.Context, s.Self, new[] { Front, BackLand });

        chosen.IsBack.Should().BeTrue("0 mana → front unaffordable → play the land face");
    }

    [Fact]
    public void FrontAffordable_DropUsed_PicksSpellFace()
    {
        var s = new BotTestScenario();
        // Three lands → {1}{U}{U} affordable (colour-blind, CMC 3 ≤ 3 sources).
        s.AddLandToBattlefield(s.Self, "Island1");
        s.AddLandToBattlefield(s.Self, "Island2");
        s.AddLandToBattlefield(s.Self, "Island3");

        // Land drop already used → only the spell face is a legal play anyway.
        var noDropCtx = new GameContext(
            s.Self, new[] { s.Self, s.Opponent }, activePlayer: s.Self,
            turnNumber: 1, currentPhase: Majik.Core.StateMachine.StepStateType.PreCombatMain,
            stack: s.Stack, landPlayAvailable: false);

        var chosen = MdfcFacePolicy.Pick(noDropCtx, s.Self, new[] { Front, BackLand });

        chosen.IsBack.Should().BeFalse("front affordable + no land drop → cast the spell");
    }

    [Fact]
    public void FrontAffordable_DropAvailable_PicksSpellFace_WhenLandsSufficient()
    {
        var s = new BotTestScenario(); // land drop available
        s.AddLandToBattlefield(s.Self, "Island1");
        s.AddLandToBattlefield(s.Self, "Island2");
        s.AddLandToBattlefield(s.Self, "Island3");

        var chosen = MdfcFacePolicy.Pick(s.Context, s.Self, new[] { Front, BackLand });

        chosen.IsBack.Should().BeFalse(
            "the spell face is affordable, so cast it rather than spend the land drop on the back");
    }

    // ── BotPlayerAgent path: drive ChooseAsync with MdfcFaceChoice candidates ──
    [Fact]
    public async Task BotPlayerAgent_ChooseAsync_ZeroMana_ReturnsBackLandFace()
    {
        var s = new BotTestScenario(); // 0 mana, land drop available
        var agent = new BotPlayerAgent(s.Self, new BotConfig(ArchetypeName: "Aggro", Strategy: "heuristic"));

        var req = new ChoiceRequest(
            ChoiceKind.PickOne,
            "Choose which face of Sink into Stupor // Soporific Springs to cast (CR 712.3)",
            Min: 1, Max: 1,
            Candidates: new object[] { Front, BackLand },
            Intent: BotIntent.None, Optional: false);

        var picked = await agent.ChooseAsync(s.Context, req, CancellationToken.None);

        picked.Should().ContainSingle();
        picked[0].Should().BeOfType<MdfcFaceChoice>();
        ((MdfcFaceChoice)picked[0]).IsBack.Should().BeTrue(
            "the BotPlayerAgent must route MDFC face prompts through MdfcFacePolicy and pick the land at 0 mana");
    }

    // ── SearchAgent path: the searched seat's ChooseAsync (capture + rollout) ──
    // SearchAgent does NOT intercept the face prompt as a SimDecision — the face
    // ChooseAsync flows straight through. Pre-fix it used the IPlayerAgent
    // interface-default ChooseAsync (first candidate → front). The override must
    // route MDFC face prompts through MdfcFacePolicy so the deadlock case resolves
    // to the land face in BOTH rollout and capture modes (same code path).
    [Fact]
    public async Task SearchAgent_ChooseAsync_ZeroMana_ReturnsBackLandFace()
    {
        var s = new BotTestScenario(); // 0 mana, land drop available
        var agent = new Majik.Bot.Search.SearchAgent(s.Self);

        var req = new ChoiceRequest(
            ChoiceKind.PickOne,
            "Choose which face of Sink into Stupor // Soporific Springs to cast (CR 712.3)",
            Min: 1, Max: 1,
            Candidates: new object[] { Front, BackLand },
            Intent: BotIntent.None, Optional: false);

        var picked = await agent.ChooseAsync(s.Context, req, CancellationToken.None);

        picked.Should().ContainSingle();
        picked[0].Should().BeOfType<MdfcFaceChoice>();
        ((MdfcFaceChoice)picked[0]).IsBack.Should().BeTrue(
            "the SearchAgent must route MDFC face prompts through MdfcFacePolicy and pick the land at 0 mana");
    }
}
