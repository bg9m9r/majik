using FluentAssertions;
using Majik.Bot.Strategies;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Bot.Tests.Strategies;

/// <summary>
/// Unit tests for <see cref="BelcherStrategy"/>.
///
/// All card constructions use the same direct-zone-manipulation pattern as
/// <see cref="DeckStrategyHelpersTests"/> — minimal objects, no real engine
/// loop, exact board states wired by hand.
///
/// Key mechanic: Goblin Charbelcher's activation cost is {3}, {T}.
/// The <see cref="DeckStrategyHelpers.BuildActivate"/> gate checks:
///   1. The artifact is on the battlefield.
///   2. All costs can be paid: ManaCostCost("{3}") checks the mana pool;
///      AdditionalCost.Tap checks !IsTapped and no summoning-sickness block.
///
/// The Belcher deck runs zero lands — mana comes from ritual chains that
/// put mana into the pool BEFORE the activation window.  Tests that wire
/// "enough mana" do so by adding mana directly to the player's pool because
/// there is no land-based source in this archetype.
/// </summary>
public sealed class BelcherStrategyTests
{
    private static BelcherStrategy Strategy() => new();

    // ── TryGetNextWinningAction — DIRECTIVE atomic kill ─────────────────────

    /// <summary>
    /// Charbelcher on board, untapped, mana pool has {3} (post-ritual state),
    /// opponent exists → returns an ActivateAbility action (the belch).
    /// </summary>
    [Fact]
    public void TryGetNextWinningAction_ReturnsActivate_WhenCharbelcherOnBoardAndManaAvailable()
    {
        var s = new BotTestScenario();

        // Put Charbelcher on the battlefield — untapped by default.
        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.AddAbility(BuildBelchAbility(belcher, s.Self));
        belcher.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);

        // Fund the {3} activation cost directly in the mana pool (post-ritual).
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.ActivateAbility>(
            "Charbelcher on board with {3} available and untapped → directive fires the belch activation");
    }

    /// <summary>
    /// Charbelcher on board but tapped → AdditionalCost.Tap.CanPay returns
    /// false → BuildActivate returns null → TryGetNextWinningAction null.
    /// </summary>
    [Fact]
    public void TryGetNextWinningAction_Null_WhenCharbelcherTapped()
    {
        var s = new BotTestScenario();

        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.AddAbility(BuildBelchAbility(belcher, s.Self));
        belcher.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);

        // Tap the Charbelcher — activation cost cannot be paid.
        belcher.Tap();

        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}"));

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("tapped Charbelcher cannot be activated — {T} cost unsatisfied");
    }

    /// <summary>
    /// Charbelcher on board, untapped, but mana pool is empty → ManaCostCost
    /// "{3}" cannot be paid → null.
    /// </summary>
    [Fact]
    public void TryGetNextWinningAction_Null_WhenInsufficientMana()
    {
        var s = new BotTestScenario();

        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.AddAbility(BuildBelchAbility(belcher, s.Self));
        belcher.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);

        // ManaPool is empty — no rituals have resolved yet.

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("mana pool is empty — {3} activation cost cannot be paid");
    }

    /// <summary>
    /// Charbelcher is in hand but not yet on the battlefield → null.
    /// The search (guided by StrategicScore) should find the cast.
    /// </summary>
    [Fact]
    public void TryGetNextWinningAction_Null_WhenCharbelcherInHandOnly()
    {
        var s = new BotTestScenario();

        s.AddCardToHand(s.Self, new Artifact("Goblin Charbelcher", "{4}"));
        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}{R}{R}{R}{R}"));

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull(
            "Charbelcher in hand but not on the battlefield — casting it is not the atomic kill; " +
            "StrategicScore steers the search toward the cast");
    }

    /// <summary>
    /// No Charbelcher anywhere — null.
    /// </summary>
    [Fact]
    public void TryGetNextWinningAction_Null_WhenNoCharbelcher()
    {
        var s = new BotTestScenario();

        s.Self.AddManaToPool(ManaCost.Parse("{R}{R}{R}{R}{R}{R}{R}"));

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("no Charbelcher anywhere — no win line available");
    }

    // ── StrategicScore ──────────────────────────────────────────────────────

    /// <summary>
    /// Charbelcher on the board should score higher than an empty board.
    /// </summary>
    [Fact]
    public void StrategicScore_HigherWhenCharbelcherOnBoard()
    {
        var s = new BotTestScenario();

        var scoreBefore = Strategy().StrategicScore(s.Context, s.Self);

        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);

        var scoreAfter = Strategy().StrategicScore(s.Context, s.Self);

        scoreAfter.Should().BeGreaterThan(scoreBefore,
            "Charbelcher on board is the highest-value assembled state (+5.0)");
    }

    /// <summary>
    /// Charbelcher in hand should score higher than an empty hand.
    /// </summary>
    [Fact]
    public void StrategicScore_HigherWhenCharbelcherInHand()
    {
        var s = new BotTestScenario();

        var scoreBefore = Strategy().StrategicScore(s.Context, s.Self);

        s.AddCardToHand(s.Self, new Artifact("Goblin Charbelcher", "{4}"));

        var scoreAfter = Strategy().StrategicScore(s.Context, s.Self);

        scoreAfter.Should().BeGreaterThan(scoreBefore,
            "Charbelcher in hand means one cast away from the kill (+2.0)");
    }

    /// <summary>
    /// Board-Charbelcher outscores hand-Charbelcher.
    /// </summary>
    [Fact]
    public void StrategicScore_BoardCharbelcherOutscoresHandCharbelcher()
    {
        var s = new BotTestScenario();

        // Board state: Charbelcher on battlefield.
        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);
        var scoreOnBoard = Strategy().StrategicScore(s.Context, s.Self);

        // Hand state: Charbelcher in hand (no board Charbelcher).
        var s2 = new BotTestScenario();
        s2.AddCardToHand(s2.Self, new Artifact("Goblin Charbelcher", "{4}"));
        var scoreInHand = Strategy().StrategicScore(s2.Context, s2.Self);

        scoreOnBoard.Should().BeGreaterThan(scoreInHand,
            "Charbelcher already on the battlefield (+5.0) is strictly more assembled " +
            "than Charbelcher in hand (+2.0)");
    }

    /// <summary>
    /// Each ritual in hand adds +0.5 to the score.
    /// </summary>
    [Fact]
    public void StrategicScore_RitualsInHandEachAddBonus()
    {
        var s = new BotTestScenario();

        var scoreEmpty = Strategy().StrategicScore(s.Context, s.Self);

        s.AddCardToHand(s.Self, new Sorcery("Desperate Ritual", "{1}{R}"));
        var scoreOneRitual = Strategy().StrategicScore(s.Context, s.Self);

        s.AddCardToHand(s.Self, new Instant("Pyretic Ritual", "{1}{R}"));
        var scoreTwoRituals = Strategy().StrategicScore(s.Context, s.Self);

        scoreOneRitual.Should().BeGreaterThan(scoreEmpty, "one ritual in hand adds +0.5");
        scoreTwoRituals.Should().BeGreaterThan(scoreOneRitual, "second ritual adds another +0.5");
        scoreTwoRituals.Should().BeApproximately(scoreEmpty + 1.0, precision: 0.01,
            "two rituals add exactly +1.0 cumulative bonus");
    }

    /// <summary>
    /// Full assembled state: Charbelcher on board + two rituals in hand.
    /// </summary>
    [Fact]
    public void StrategicScore_MaxWhenFullLineAssembled()
    {
        var s = new BotTestScenario();

        var belcher = new Artifact("Goblin Charbelcher", "{4}");
        belcher.ChangeOwner(s.Self);
        s.Self.Zones.Battlefield.AddCard(belcher);

        s.AddCardToHand(s.Self, new Sorcery("Desperate Ritual", "{1}{R}"));
        s.AddCardToHand(s.Self, new Instant("Manamorphose", "{1}{R}"));

        var score = Strategy().StrategicScore(s.Context, s.Self);

        // Charbelcher on board (+5.0) + two rituals (+0.5 each) = 6.0
        score.Should().BeApproximately(6.0, precision: 0.01,
            "full line: board Charbelcher (+5.0) + 2 rituals (+1.0) = 6.0");
    }

    // ── AdviseMulligan ──────────────────────────────────────────────────────

    /// <summary>
    /// Hand with Charbelcher alone → keep (payoff in hand).
    /// </summary>
    [Fact]
    public void AdviseMulligan_Keeps_WhenCharbelcherInHand()
    {
        var hand = new List<ICard>
        {
            new Artifact("Goblin Charbelcher", "{4}"),
            new Sorcery("Desperate Ritual", "{1}{R}"),
            new Instant("Pyretic Ritual", "{1}{R}"),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Keep,
            "Charbelcher in hand is sufficient to keep — the win condition is present");
    }

    /// <summary>
    /// Hand with two rituals → keep (mana engine to find the Charbelcher).
    /// </summary>
    [Fact]
    public void AdviseMulligan_Keeps_WhenTwoRitualsInHand()
    {
        var hand = new List<ICard>
        {
            new Sorcery("Desperate Ritual", "{1}{R}"),
            new Instant("Pyretic Ritual", "{1}{R}"),
            new Instant("Manamorphose", "{1}{R}"),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Keep,
            "two rituals = mana engine; keep and dig for Charbelcher");
    }

    /// <summary>
    /// Hand with only one ritual and no Charbelcher → mulligan.
    /// </summary>
    [Fact]
    public void AdviseMulligan_Mulligans_WhenOnlyOneRitual()
    {
        var hand = new List<ICard>
        {
            new Sorcery("Desperate Ritual", "{1}{R}"),
            new Sorcery("Witch Enchanter", "{R}"),
            new Sorcery("Pinnacle Monk", "{R}"),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Mulligan,
            "one ritual cannot generate enough mana for Charbelcher; ship it");
    }

    /// <summary>
    /// Empty hand (no Charbelcher, no rituals) → mulligan.
    /// </summary>
    [Fact]
    public void AdviseMulligan_Mulligans_WhenNoComboElements()
    {
        var hand = new List<ICard>
        {
            new Sorcery("Witch Enchanter", "{R}"),
            new Sorcery("Pinnacle Monk", "{R}"),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Mulligan,
            "no combo elements in hand — nothing to work with");
    }

    /// <summary>
    /// After ≥ 3 mulligans → returns null (defer to generic policy).
    /// </summary>
    [Fact]
    public void AdviseMulligan_ReturnsNull_AtHighMulliganDepth()
    {
        var hand = new List<ICard>
        {
            new Sorcery("Witch Enchanter", "{R}"),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 3);

        decision.Should().BeNull("strategy defers to generic policy at ≥ 3 mulligans taken");
    }

    /// <summary>
    /// Irencrag Feat counts as a ritual for the keep decision.
    /// </summary>
    [Fact]
    public void AdviseMulligan_Keeps_WhenIrencragFeatCountsAsRitual()
    {
        var hand = new List<ICard>
        {
            new Sorcery("Irencrag Feat", "{1}{R}{R}"),
            new Instant("Manamorphose", "{1}{R}"),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Keep,
            "Irencrag Feat + Manamorphose = two rituals; keep it");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a minimal Charbelcher activated ability with the real cost shape
    /// ({3}, {T}) and no effect, so cost-payability can be tested without
    /// driving the full engine.
    ///
    /// Mirrors the structure in <see cref="Majik.Core.CardData.Factories.GoblinCharbelcherFactory"/>:
    /// ManaCostCost("{3}") + AdditionalCost.Tap(belcher), one target request.
    /// </summary>
    private static Majik.Core.Abilities.ActivatedAbility BuildBelchAbility(
        Artifact belcher, Majik.Core.Players.Player owner)
    {
        return new Majik.Core.Abilities.ActivatedAbility(
            source: belcher,
            controller: owner,
            costs: new Majik.Core.Costs.ICost[]
            {
                new ManaCostCost("{3}"),
                AdditionalCost.Tap(belcher),
            },
            effects: Array.Empty<Majik.Core.Abilities.IEffect>(),
            targetRequests: new[]
            {
                new Majik.Core.Players.Agents.TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });
    }
}
