using FluentAssertions;
using Majik.Bot.Evaluation;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests;

public class BoardEvalTests
{
    [Fact]
    public void Score_IncreasesWith_SelfLife()
    {
        var s1 = new BotTestScenario(selfLife: 10);
        var s2 = new BotTestScenario(selfLife: 20);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }

    [Fact]
    public void Score_DecreasesWith_OpponentLife()
    {
        var s1 = new BotTestScenario(oppLife: 10);
        var s2 = new BotTestScenario(oppLife: 20);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s1.Context, s1.Self, w).Should().BeGreaterThan(BoardEval.Score(s2.Context, s2.Self, w));
    }

    [Fact]
    public void Score_IncreasesWith_BotBoardPower()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddCreatureToBattlefield(s2.Self, "Grizzly Bears", 2, 2);
        var w = ArchetypeWeights.Prowess;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }

    [Fact]
    public void Score_DecreasesWith_OpponentBoardPower()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddCreatureToBattlefield(s2.Opponent, "Tarmogoyf", 4, 5);
        var w = ArchetypeWeights.Burn;
        BoardEval.Score(s1.Context, s1.Self, w).Should().BeGreaterThan(BoardEval.Score(s2.Context, s2.Self, w));
    }

    [Fact]
    public void Score_IncreasesWith_ManaSources()
    {
        var s1 = new BotTestScenario();
        var s2 = new BotTestScenario();
        s2.AddLandToBattlefield(s2.Self, "Mountain");
        s2.AddLandToBattlefield(s2.Self, "Mountain");
        var w = ArchetypeWeights.BorosEnergy;
        BoardEval.Score(s2.Context, s2.Self, w).Should().BeGreaterThan(BoardEval.Score(s1.Context, s1.Self, w));
    }

    // ── Lethal-proximity term tests ─────────────────────────────────────────

    /// <summary>
    /// Opp at 3 life must score higher than opp at 15 life (all else equal).
    /// The lethal-proximity term should make the eval point the bot toward
    /// positions where the opponent is closer to losing.
    /// </summary>
    [Fact]
    public void Score_IsHigher_WhenOpponentCloserToLethal()
    {
        var near   = new BotTestScenario(oppLife: 3);   // opp nearly dead
        var safe   = new BotTestScenario(oppLife: 15);  // opp safe
        var w = ArchetypeWeights.Prowess;

        BoardEval.Score(near.Context, near.Self, w)
            .Should().BeGreaterThan(
                BoardEval.Score(safe.Context, safe.Self, w),
                because: "the eval should reward driving the opponent toward lethal");
    }

    /// <summary>
    /// Non-linearity test: the marginal gain of going 15→13 life (2 points
    /// in the safe zone) must be less than the marginal gain of going 3→1
    /// life (2 points in the danger zone). This validates the quadratic ramp.
    /// </summary>
    [Fact]
    public void Score_NonLinear_DangerZoneDamageMoreValuable()
    {
        var w = ArchetypeWeights.Prowess;

        // Two-point damage in the safe zone: opp 15→13
        var safe15 = new BotTestScenario(oppLife: 15);
        var safe13 = new BotTestScenario(oppLife: 13);
        double safeDelta = BoardEval.Score(safe13.Context, safe13.Self, w)
                         - BoardEval.Score(safe15.Context, safe15.Self, w);

        // Two-point damage in the danger zone: opp 3→1
        var low3 = new BotTestScenario(oppLife: 3);
        var low1 = new BotTestScenario(oppLife: 1);
        double dangerDelta = BoardEval.Score(low1.Context, low1.Self, w)
                           - BoardEval.Score(low3.Context, low3.Self, w);

        dangerDelta.Should().BeGreaterThan(safeDelta,
            because: "each point of damage near lethal (3→1) must be worth more " +
                     "than the same 2 points in the safe zone (15→13) — quadratic ramp");
    }

    /// <summary>
    /// LethalProximityBonus is zero at starting life (20) and grows as
    /// opp life decreases — basic monotonicity check on the helper itself.
    /// </summary>
    [Theory]
    [InlineData(20, 15)]
    [InlineData(15, 10)]
    [InlineData(10, 5)]
    [InlineData(5, 3)]
    [InlineData(3, 1)]
    public void LethalProximityBonus_IsStrictlyMonotone(int higherLife, int lowerLife)
    {
        BoardEval.LethalProximityBonus(lowerLife)
            .Should().BeGreaterThan(
                BoardEval.LethalProximityBonus(higherLife),
                because: $"proximity bonus at {lowerLife} must exceed bonus at {higherLife}");
    }

    /// <summary>
    /// Validate the concrete bonus values documented in the BoardEval XML comment.
    /// This guards the formula against accidental regressions in the constants.
    /// </summary>
    [Theory]
    [InlineData(20, 0)]   // baseline: no bonus at starting life
    [InlineData(15, 5)]   // linear only: 20-15 = 5
    [InlineData(10, 10)]  // linear only: 20-10 = 10
    [InlineData(5, 15)]   // threshold: 20-5 = 15 + (5-5)^2 = 0 → 15
    [InlineData(3, 21)]   // ramp: 20-3=17 + (5-3)^2=4 → 21
    [InlineData(1, 35)]   // ramp: 20-1=19 + (5-1)^2=16 → 35
    public void LethalProximityBonus_MatchesDocumentedValues(int oppLife, double expectedBonus)
    {
        BoardEval.LethalProximityBonus(oppLife)
            .Should().BeApproximately(expectedBonus, precision: 0.001,
                because: $"LethalProximityBonus({oppLife}) should be {expectedBonus} per the formula docs");
    }

    // ── Card-advantage differential term tests ──────────────────────────────

    /// <summary>
    /// Bot ahead on cards-in-hand (5 vs 2) must score higher than being behind
    /// (2 vs 5), all else equal. This is the cardinal property of the
    /// card-advantage differential term — it must move in the right direction.
    /// </summary>
    [Fact]
    public void Score_IsHigher_WhenBotAheadOnCards()
    {
        var w = ArchetypeWeights.AzoriusControl;

        // ahead: bot 5 cards, opp 2
        var ahead = new BotTestScenario();
        for (int i = 0; i < 5; i++)
            ahead.AddCardToHand(ahead.Self, new Majik.Core.Cards.Instant("Counterspell", "UU"));
        for (int i = 0; i < 2; i++)
            ahead.AddCardToHand(ahead.Opponent, new Majik.Core.Cards.Instant("Counterspell", "UU"));

        // behind: bot 2 cards, opp 5
        var behind = new BotTestScenario();
        for (int i = 0; i < 2; i++)
            behind.AddCardToHand(behind.Self, new Majik.Core.Cards.Instant("Counterspell", "UU"));
        for (int i = 0; i < 5; i++)
            behind.AddCardToHand(behind.Opponent, new Majik.Core.Cards.Instant("Counterspell", "UU"));

        BoardEval.Score(ahead.Context, ahead.Self, w)
            .Should().BeGreaterThan(
                BoardEval.Score(behind.Context, behind.Self, w),
                because: "being up 3 cards (5 vs 2) should score higher than being down 3 cards (2 vs 5)");
    }

    /// <summary>
    /// The card-advantage differential must be symmetric — a differential of
    /// +3 from the bot's perspective should beat parity (0), which should beat -3.
    /// </summary>
    [Fact]
    public void Score_CardAdvantage_IsMonotoneInDifferential()
    {
        var w = ArchetypeWeights.AzoriusControl;

        // parity: 4 vs 4
        var parity = new BotTestScenario();
        for (int i = 0; i < 4; i++)
        {
            parity.AddCardToHand(parity.Self,     new Majik.Core.Cards.Instant("Counterspell", "UU"));
            parity.AddCardToHand(parity.Opponent, new Majik.Core.Cards.Instant("Counterspell", "UU"));
        }

        // up 3: 7 vs 4
        var up = new BotTestScenario();
        for (int i = 0; i < 7; i++)
            up.AddCardToHand(up.Self, new Majik.Core.Cards.Instant("Counterspell", "UU"));
        for (int i = 0; i < 4; i++)
            up.AddCardToHand(up.Opponent, new Majik.Core.Cards.Instant("Counterspell", "UU"));

        // down 3: 1 vs 4
        var down = new BotTestScenario();
        down.AddCardToHand(down.Self, new Majik.Core.Cards.Instant("Counterspell", "UU"));
        for (int i = 0; i < 4; i++)
            down.AddCardToHand(down.Opponent, new Majik.Core.Cards.Instant("Counterspell", "UU"));

        double scoreUp     = BoardEval.Score(up.Context,     up.Self,     w);
        double scoreParity = BoardEval.Score(parity.Context, parity.Self, w);
        double scoreDown   = BoardEval.Score(down.Context,   down.Self,   w);

        scoreUp.Should().BeGreaterThan(scoreParity,
            because: "up 3 cards must beat parity for a control archetype");
        scoreParity.Should().BeGreaterThan(scoreDown,
            because: "parity must beat being down 3 cards");
    }

    /// <summary>
    /// The card-advantage term must be negligible for aggro (Burn): an aggro
    /// deck is frequently empty-handed by design, so we must not penalise it
    /// heavily for running out its hand.
    /// </summary>
    [Fact]
    public void Score_CardAdvantage_IsLowWeight_ForAggro()
    {
        var wControl = ArchetypeWeights.AzoriusControl;
        var wBurn    = ArchetypeWeights.Burn;

        // Same setup: bot up 4 cards vs opponent (7 vs 3)
        var s = new BotTestScenario();
        for (int i = 0; i < 7; i++)
            s.AddCardToHand(s.Self, new Majik.Core.Cards.Instant("Lightning Bolt", "R"));
        for (int i = 0; i < 3; i++)
            s.AddCardToHand(s.Opponent, new Majik.Core.Cards.Instant("Lightning Bolt", "R"));

        // A zero-base (no cards either side) for delta isolation
        var baseline = new BotTestScenario();

        double controlDelta = BoardEval.Score(s.Context, s.Self, wControl)
                            - BoardEval.Score(baseline.Context, baseline.Self, wControl);
        double burnDelta = BoardEval.Score(s.Context, s.Self, wBurn)
                         - BoardEval.Score(baseline.Context, baseline.Self, wBurn);

        // Control must value the card-advantage differential more than burn does
        controlDelta.Should().BeGreaterThan(burnDelta,
            because: "AzoriusControl weights card advantage much higher than Burn");
    }

    // ── Planeswalker-engine term tests ──────────────────────────────────────

    /// <summary>
    /// Having a planeswalker in play (Teferi at 5 loyalty) must raise the eval
    /// score for AzoriusControl, because loyalty = accumulated card advantage.
    /// </summary>
    [Fact]
    public void Score_IsHigher_WithPlaneswalkerOnBoard()
    {
        var w = ArchetypeWeights.AzoriusControl;

        var withWalker    = new BotTestScenario();
        withWalker.AddPlaneswalkerToBattlefield(withWalker.Self, "Teferi, Time Raveler", loyalty: 5);

        var withoutWalker = new BotTestScenario();

        BoardEval.Score(withWalker.Context, withWalker.Self, w)
            .Should().BeGreaterThan(
                BoardEval.Score(withoutWalker.Context, withoutWalker.Self, w),
                because: "a planeswalker at loyalty 5 should add PlaneswalkerEngine bonus");
    }

    /// <summary>
    /// A planeswalker at higher loyalty must score better than the same
    /// planeswalker at lower loyalty, all else equal.
    /// </summary>
    [Fact]
    public void Score_IsHigher_WithHigherLoyaltyPlaneswalker()
    {
        var w = ArchetypeWeights.AzoriusControl;

        var highLoyalty = new BotTestScenario();
        highLoyalty.AddPlaneswalkerToBattlefield(highLoyalty.Self, "Teferi, Hero of Dominaria", loyalty: 8);

        var lowLoyalty = new BotTestScenario();
        lowLoyalty.AddPlaneswalkerToBattlefield(lowLoyalty.Self, "Teferi, Hero of Dominaria", loyalty: 3);

        BoardEval.Score(highLoyalty.Context, highLoyalty.Self, w)
            .Should().BeGreaterThan(
                BoardEval.Score(lowLoyalty.Context, lowLoyalty.Self, w),
                because: "8 loyalty represents more accumulated value than 3 loyalty");
    }

    // ── Strategic bonus term tests ──────────────────────────────────────────

    /// <summary>
    /// When a deck strategy returns a positive strategic score, the eval with
    /// that strategy must exceed the baseline (no strategy) by exactly
    /// <c>weights.Strategic * strategyScore</c>.
    /// </summary>
    [Fact]
    public void Score_IncludesStrategicBonus_WhenDeckStrategyProvided()
    {
        var s = new BotTestScenario();   // self/opp, neutral board
        var weights = ArchetypeWeights.Default with { Strategic = 1.0 };
        var withStrat = new StubStrategy(7.0);

        var baseline = BoardEval.Score(s.Context, s.Self, weights, deck: null);
        var boosted  = BoardEval.Score(s.Context, s.Self, weights, deck: withStrat);

        (boosted - baseline).Should().BeApproximately(7.0, 1e-9);
    }

    /// <summary>
    /// A2 NEUTRALITY GUARD (deck-strategy framework landing). The Strategic term
    /// must contribute EXACTLY ZERO — leaving the eval byte-identical to the
    /// pre-framework engine — for any archetype that has NO registered
    /// <see cref="Majik.Bot.Strategies.IDeckStrategy"/>. Neutrality is structural,
    /// not weight-based: <c>weights.Strategic</c> is 1.0 by default, but the term is
    /// <c>weights.Strategic * (deck?.StrategicScore(...) ?? 0.0)</c>, so an
    /// unregistered archetype resolves <c>deck == null</c> via
    /// <see cref="Majik.Bot.Strategies.DeckStrategyRegistry"/> and the product is 0.
    ///
    /// <para>This proves the framework does NOT shift the play/eval of existing
    /// decks (Burn / Prowess / BorosEnergy / AzoriusControl / and every Default
    /// archetype such as Sultai / EldraziTron). If a strategy were ever
    /// accidentally registered for one of these names, the registry lookup would
    /// return non-null and this test would fail loudly.</para>
    /// </summary>
    [Theory]
    [InlineData("Burn")]
    [InlineData("Prowess")]
    [InlineData("BorosEnergy")]
    [InlineData("AzoriusControl")]
    [InlineData("Sultai")]
    [InlineData("EldraziTron")]
    public void Score_StrategicTerm_IsNeutral_ForArchetypesWithoutRegisteredStrategy(string archetype)
    {
        // The registry is the production resolution path used by both
        // HeuristicStrategy and SearchStrategy: deck = registry.For(archetypeName).
        var deck = Majik.Bot.Strategies.DeckStrategyRegistry.For(archetype);
        deck.Should().BeNull(
            because: $"'{archetype}' has no [DeckStrategy] registered — Phase A ships the seam only");

        var s = new BotTestScenario();
        // Strategic = 1.0 (the live default) so the ONLY thing that could move the
        // score is a non-null strategy. Prove it does not.
        var weights = ArchetypeWeights.ForArchetype(archetype);

        var withTermActive = BoardEval.Score(s.Context, s.Self, weights, deck: deck);
        var preFramework    = BoardEval.Score(s.Context, s.Self, weights, deck: null);

        withTermActive.Should().Be(preFramework,
            because: "with no registered strategy the Strategic term must be exactly 0 — eval byte-identical to pre-framework");
    }

    private sealed class StubStrategy : Majik.Bot.Strategies.IDeckStrategy
    {
        private readonly double _v;
        public StubStrategy(double v) => _v = v;
        public double StrategicScore(GameContext ctx, Player self) => _v;
        public PriorityAction? TryGetNextWinningAction(GameContext ctx, Player self) => null;
        public MulliganDecision? AdviseMulligan(IReadOnlyList<ICard> hand, int n) => null;
    }

    // ── Non-regression: existing dominant terms are not overridden ──────────

    /// <summary>
    /// Being far ahead on board and life must still dominate a small card
    /// deficit. The new card-advantage terms are gradients, not win conditions —
    /// the terminal win/loss dominates and board/life dominates a card-count gap.
    ///
    /// Scenario: bot is up 4 life (20 vs 16), has a 4/4 creature, but is down
    /// 2 cards in hand. For AzoriusControl the card-down penalty is
    /// 2 × 3.0 = 6, while the board+life gains exceed it.
    /// </summary>
    [Fact]
    public void Score_BoardAndLifeAdvantage_DominatesSmallCardDeficit()
    {
        var w = ArchetypeWeights.AzoriusControl;

        // Bot strong: 20 vs 16 life, has a 4/4 on board, but down 2 cards
        var strong = new BotTestScenario(selfLife: 20, oppLife: 16);
        strong.AddCreatureToBattlefield(strong.Self, "Solitude", 3, 3);
        strong.AddCardToHand(strong.Opponent, new Majik.Core.Cards.Instant("Counterspell", "UU"));
        strong.AddCardToHand(strong.Opponent, new Majik.Core.Cards.Instant("Counterspell", "UU"));
        strong.AddCardToHand(strong.Opponent, new Majik.Core.Cards.Instant("Counterspell", "UU"));
        strong.AddCardToHand(strong.Self,     new Majik.Core.Cards.Instant("Counterspell", "UU"));

        // Baseline: equal life, no board, no cards
        var baseline = new BotTestScenario();

        BoardEval.Score(strong.Context, strong.Self, w)
            .Should().BeGreaterThan(
                BoardEval.Score(baseline.Context, baseline.Self, w),
                because: "having 4-life lead + 3/3 creature must outweigh a 2-card deficit");
    }
}
