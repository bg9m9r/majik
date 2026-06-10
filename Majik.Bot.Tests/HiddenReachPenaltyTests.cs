using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Tests.Helpers;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Bot.Tests;

/// <summary>
/// Tests for <see cref="BoardEval.HiddenReachPenalty"/> — the eval term that
/// penalizes being within burn reach of the opponent's (sandbox) hand. In
/// determinized worlds that hand is SAMPLED (the bot's own honest guess), so
/// dangerous sampled worlds must evaluate as dangerous.
///
/// Hand cards are built via the SAME prod-equivalent path the determinization
/// sampler uses (<see cref="ScryfallCardFactory"/>) so oracle-text lookup by
/// name in <c>DirectDamageRecognizer</c> resolves against real seed rows.
/// </summary>
public class HiddenReachPenaltyTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private static readonly ScryfallCardFactory Factory = new(Repo);

    private static ICard Build(string name, Player owner) => Factory.Create(name, owner);

    private static void AddToOppHand(BotTestScenario s, string name)
        => s.Opponent.Zones.Hand.AddCard(Build(name, s.Opponent));

    // ── Core formula ─────────────────────────────────────────────────────────

    /// <summary>
    /// Opp hand: 3x Lightning Bolt ({R}, "deals 3 damage to any target") = 9 reach.
    /// Opp has 3 Mountains → mv 1 &lt;= 3 + 2, all castable soon. Self at 9 life:
    /// penalty = max(0, 9 - (9 - 1)) = 1.
    /// </summary>
    [Fact]
    public void HiddenReachPenalty_ThreeBolts_SelfAtNine_IsExactlyOne()
    {
        var s = new BotTestScenario(selfLife: 9);
        for (var i = 0; i < 3; i++)
            AddToOppHand(s, "Lightning Bolt");
        for (var i = 0; i < 3; i++)
            s.AddLandToBattlefield(s.Opponent, "Mountain");

        BoardEval.HiddenReachPenalty(s.Self, s.Opponent).Should().Be(1,
            because: "reach 9 vs (9 life - 1 margin) = 8 leaves exactly 1 point of overlap");
    }

    /// <summary>Same 9-reach hand, self at a healthy 20 life → no penalty.</summary>
    [Fact]
    public void HiddenReachPenalty_ThreeBolts_SelfAtTwenty_IsZero()
    {
        var s = new BotTestScenario(selfLife: 20);
        for (var i = 0; i < 3; i++)
            AddToOppHand(s, "Lightning Bolt");
        for (var i = 0; i < 3; i++)
            s.AddLandToBattlefield(s.Opponent, "Mountain");

        BoardEval.HiddenReachPenalty(s.Self, s.Opponent).Should().Be(0,
            because: "9 reach cannot threaten 20 life");
    }

    // ── Mana gating (castable soon = mv <= opp lands + 2) ───────────────────

    /// <summary>
    /// Lightning Bolt is mv 1: even with ZERO opponent lands it passes the gate
    /// (1 &lt;= 0 + 2) and is counted. Self at 1 life → penalty = max(0, 3 - 0) = 3.
    /// </summary>
    [Fact]
    public void HiddenReachPenalty_CheapBurn_NoLands_StillCounted()
    {
        var s = new BotTestScenario(selfLife: 1);
        AddToOppHand(s, "Lightning Bolt");

        BoardEval.HiddenReachPenalty(s.Self, s.Opponent).Should().Be(3,
            because: "mv 1 <= 0 lands + 2, so Bolt counts even off zero lands");
    }

    /// <summary>
    /// Exquisite Firecraft ({1}{R}{R}, mv 3, "deals 4 damage to any target") with
    /// ZERO opponent lands is gated out (3 &gt; 0 + 2) → no penalty even at 1 life.
    /// </summary>
    [Fact]
    public void HiddenReachPenalty_ExpensiveBurn_NoLands_GatedOut()
    {
        var s = new BotTestScenario(selfLife: 1);
        AddToOppHand(s, "Exquisite Firecraft");

        BoardEval.HiddenReachPenalty(s.Self, s.Opponent).Should().Be(0,
            because: "mv 3 > 0 lands + 2 — not castable soon, so it adds no reach");
    }

    /// <summary>
    /// Same Exquisite Firecraft becomes live once the opponent has a land:
    /// mv 3 &lt;= 1 + 2 → reach 4, self at 1 life → penalty = max(0, 4 - 0) = 4.
    /// </summary>
    [Fact]
    public void HiddenReachPenalty_ExpensiveBurn_EnoughLands_Counted()
    {
        var s = new BotTestScenario(selfLife: 1);
        AddToOppHand(s, "Exquisite Firecraft");
        s.AddLandToBattlefield(s.Opponent, "Mountain");

        BoardEval.HiddenReachPenalty(s.Self, s.Opponent).Should().Be(4,
            because: "mv 3 <= 1 land + 2 brings the 4-damage spell into reach");
    }

    // ── Non-damage hands ─────────────────────────────────────────────────────

    /// <summary>A hand with no burn (creature + land) is harmless at any life total.</summary>
    [Fact]
    public void HiddenReachPenalty_NonDamageHand_LowLife_IsZero()
    {
        var s = new BotTestScenario(selfLife: 5);
        AddToOppHand(s, "Grizzly Bears");
        AddToOppHand(s, "Island");
        for (var i = 0; i < 4; i++)
            s.AddLandToBattlefield(s.Opponent, "Mountain");

        BoardEval.HiddenReachPenalty(s.Self, s.Opponent).Should().Be(0,
            because: "neither Grizzly Bears nor Island deals direct damage to a player");
    }

    // ── Score wiring + kill-switch ───────────────────────────────────────────

    /// <summary>
    /// The term is wired into <see cref="BoardEval.Score"/>: on a reach-dangerous
    /// board a positive HiddenReach weight must REDUCE the searched seat's score
    /// relative to the same board scored with the weight zeroed.
    /// </summary>
    [Fact]
    public void Score_DangerousBoard_PenalizedWhenWeightPositive()
    {
        var s = new BotTestScenario(selfLife: 6);
        for (var i = 0; i < 3; i++)
            AddToOppHand(s, "Lightning Bolt");
        for (var i = 0; i < 3; i++)
            s.AddLandToBattlefield(s.Opponent, "Mountain");

        var wOn = ArchetypeWeights.Default with { HiddenReach = 1.0 };
        var wOff = ArchetypeWeights.Default with { HiddenReach = 0.0 };

        BoardEval.Score(s.Context, s.Self, wOn)
            .Should().BeLessThan(BoardEval.Score(s.Context, s.Self, wOff),
                because: "being within sampled burn reach must reduce the score");
    }

    /// <summary>
    /// Kill-switch: with HiddenReach = 0 the score on a reach-dangerous board is
    /// byte-identical to the same board with the burn swapped for harmless cards
    /// (same hand COUNT, so every other term is unchanged) — proving 0 fully
    /// disables the term.
    /// </summary>
    [Fact]
    public void Score_WeightZero_DisablesTerm_ByteIdentical()
    {
        var w = ArchetypeWeights.Default with { HiddenReach = 0.0 };

        var dangerous = new BotTestScenario(selfLife: 6);
        for (var i = 0; i < 3; i++)
            AddToOppHand(dangerous, "Lightning Bolt");
        for (var i = 0; i < 3; i++)
            dangerous.AddLandToBattlefield(dangerous.Opponent, "Mountain");

        var harmless = new BotTestScenario(selfLife: 6);
        for (var i = 0; i < 3; i++)
            AddToOppHand(harmless, "Grizzly Bears");
        for (var i = 0; i < 3; i++)
            harmless.AddLandToBattlefield(harmless.Opponent, "Mountain");

        BoardEval.Score(dangerous.Context, dangerous.Self, w)
            .Should().Be(BoardEval.Score(harmless.Context, harmless.Self, w),
                because: "HiddenReach = 0 must make burn-in-hand invisible to the eval");
    }
}
