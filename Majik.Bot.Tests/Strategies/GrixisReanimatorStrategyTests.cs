using FluentAssertions;
using Majik.Bot.Strategies;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Bot.Tests.Strategies;

/// <summary>
/// Unit tests for <see cref="GrixisReanimatorStrategy"/>.
///
/// All card constructions use the same direct-zone manipulation pattern as
/// <see cref="DeckStrategyHelpersTests"/> — minimal objects, no real engine
/// loop, exact board states wired by hand.
/// </summary>
public sealed class GrixisReanimatorStrategyTests
{
    private static GrixisReanimatorStrategy Strategy() => new();

    // ── TryGetNextWinningAction — advisory-only (always null) ───────────────
    //
    // Reanimation is a multi-turn ENGINE, not an atomic kill. Directive
    // override over-commits the bot and loses (measured: 20 % win-rate,
    // combo fired 12/16 games). The method always returns null; StrategicScore
    // steers the MCTS search toward assembling and executing the plan instead.
    //
    // Each test below verifies advisory-only behaviour in a board state that
    // the OLD directive code would have returned a CastSpell action for.

    [Fact]
    public void TryGetNextWinningAction_AlwaysNull_AdvisoryOnly_EngineCombo_FullLineAssembled_Persist()
    {
        // Emperor + Archon in yard, Persist in hand, 3 lands — previously
        // would have returned CastSpell(Persist); now must return null because
        // reanimation is a multi-turn engine, not an atomic kill.
        var s = new BotTestScenario();

        var emperor = new Creature("Emperor of Bones", manaCost: "{1}{B}", power: 2, toughness: 2);
        emperor.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(emperor);

        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        s.AddCardToHand(s.Self, new Sorcery("Persist", manaCost: "{2}{B}"));
        s.AddLandToBattlefield(s.Self, "Swamp1");
        s.AddLandToBattlefield(s.Self, "Swamp2");
        s.AddLandToBattlefield(s.Self, "Swamp3");

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull(
            "GrixisReanimator is advisory-only: reanimation is a multi-turn engine, " +
            "not an atomic kill — directive override over-commits and loses");
    }

    [Fact]
    public void TryGetNextWinningAction_AlwaysNull_AdvisoryOnly_EngineCombo_Unearth()
    {
        // Emperor + Archon in yard, Unearth in hand, 1 land — previously
        // would have returned CastSpell(Unearth); now always null.
        var s = new BotTestScenario();

        var emperor = new Creature("Emperor of Bones", manaCost: "{1}{B}", power: 2, toughness: 2);
        emperor.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(emperor);

        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        s.AddCardToHand(s.Self, new Sorcery("Unearth", manaCost: "{B}"));
        s.AddLandToBattlefield(s.Self, "Swamp");

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("advisory-only: always returns null regardless of board state");
    }

    [Fact]
    public void TryGetNextWinningAction_AlwaysNull_AdvisoryOnly_PartialLine_NoEmperor()
    {
        // Archon in yard, Persist in hand, no Emperor — previously null for a
        // different reason; now always null for the advisory-only reason.
        var s = new BotTestScenario();

        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        s.AddCardToHand(s.Self, new Sorcery("Persist", manaCost: "{2}{B}"));
        s.AddLandToBattlefield(s.Self, "Swamp1");
        s.AddLandToBattlefield(s.Self, "Swamp2");
        s.AddLandToBattlefield(s.Self, "Swamp3");

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("advisory-only: always returns null");
    }

    [Fact]
    public void TryGetNextWinningAction_AlwaysNull_AdvisoryOnly_NoReanimateSpell()
    {
        // All graveyard pieces but no spell in hand — always null.
        var s = new BotTestScenario();

        var emperor = new Creature("Emperor of Bones", manaCost: "{1}{B}", power: 2, toughness: 2);
        emperor.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(emperor);

        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        s.AddLandToBattlefield(s.Self, "Swamp1");
        s.AddLandToBattlefield(s.Self, "Swamp2");
        s.AddLandToBattlefield(s.Self, "Swamp3");

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("advisory-only: always returns null");
    }

    [Fact]
    public void TryGetNextWinningAction_AlwaysNull_AdvisoryOnly_InsufficientMana()
    {
        // Full line assembled but only 2 lands (can't cast Persist) — always null.
        var s = new BotTestScenario();

        var emperor = new Creature("Emperor of Bones", manaCost: "{1}{B}", power: 2, toughness: 2);
        emperor.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(emperor);

        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        s.AddCardToHand(s.Self, new Sorcery("Persist", manaCost: "{2}{B}"));
        s.AddLandToBattlefield(s.Self, "Swamp1");
        s.AddLandToBattlefield(s.Self, "Swamp2");

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("advisory-only: always returns null");
    }

    [Fact]
    public void TryGetNextWinningAction_AlwaysNull_AdvisoryOnly_SetupPhase_FaithlessLootingInHand()
    {
        // Archon not in yard yet, Faithless Looting in hand — previously
        // would have returned CastSpell(Faithless Looting); now always null.
        // StrategicScore will steer the search toward casting the enabler.
        var s = new BotTestScenario();

        s.AddCardToHand(s.Self, new Sorcery("Faithless Looting", manaCost: "{R}"));
        s.AddLandToBattlefield(s.Self, "Mountain");

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull(
            "advisory-only: StrategicScore steers toward enablers; " +
            "directive override is for atomic kills only");
    }

    // ── StrategicScore ──────────────────────────────────────────────────────

    [Fact]
    public void StrategicScore_HigherWhenArchonInGraveyard()
    {
        var s = new BotTestScenario();

        // Baseline: empty graveyard.
        var scoreEmpty = Strategy().StrategicScore(s.Context, s.Self);

        // Add Archon to graveyard.
        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        var scoreWithArchon = Strategy().StrategicScore(s.Context, s.Self);

        scoreWithArchon.Should().BeGreaterThan(scoreEmpty,
            "Archon in graveyard is the key assembled piece — score should increase");
    }

    [Fact]
    public void StrategicScore_HigherWithReanimationSpellInHand()
    {
        var s = new BotTestScenario();

        var scoreBefore = Strategy().StrategicScore(s.Context, s.Self);

        s.AddCardToHand(s.Self, new Sorcery("Persist", manaCost: "{2}{B}"));

        var scoreAfter = Strategy().StrategicScore(s.Context, s.Self);

        scoreAfter.Should().BeGreaterThan(scoreBefore,
            "Persist in hand raises the strategic score — one half of the two-piece line is present");
    }

    [Fact]
    public void StrategicScore_MaxWhenFullLineAssembled()
    {
        // Archon in yard + Emperor in yard + Persist in hand = all bonuses.
        var s = new BotTestScenario();

        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        var emperor = new Creature("Emperor of Bones", manaCost: "{1}{B}", power: 2, toughness: 2);
        emperor.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(emperor);

        s.AddCardToHand(s.Self, new Sorcery("Persist", manaCost: "{2}{B}"));

        var score = Strategy().StrategicScore(s.Context, s.Self);

        // Archon in yard (+3.0) + Persist in hand (+1.5) + Emperor in yard (+1.0) = 5.5
        score.Should().BeApproximately(5.5, precision: 0.01,
            "all three scoring bonuses apply when line is fully assembled");
    }

    // ── AdviseMulligan ──────────────────────────────────────────────────────

    [Fact]
    public void AdviseMulligan_KeepsHand_WithLandAndEnabler()
    {
        // A land + Faithless Looting: meets the minimum keep criteria.
        var hand = new List<ICard>
        {
            new Land("Swamp"),
            new Sorcery("Faithless Looting", manaCost: "{R}"),
            new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Keep,
            "hand has a land and a functional enabler — keep it");
    }

    [Fact]
    public void AdviseMulligan_KeepsHand_WithPersistAndLand()
    {
        var hand = new List<ICard>
        {
            new Land("Swamp"),
            new Land("Island"),
            new Sorcery("Persist", manaCost: "{2}{B}"),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Keep,
            "Persist in hand counts as a functional piece");
    }

    [Fact]
    public void AdviseMulligan_MulligansHand_WithNoLand()
    {
        // All spells, no land — can't cast anything.
        var hand = new List<ICard>
        {
            new Sorcery("Faithless Looting", manaCost: "{R}"),
            new Sorcery("Persist", manaCost: "{2}{B}"),
            new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6),
            new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Mulligan,
            "no land means the deck is bricked on turn 1");
    }

    [Fact]
    public void AdviseMulligan_MulligansHand_WithLand_ButNoEnabler()
    {
        // Lands + Archons only — no enabler or reanimation spell.
        var hand = new List<ICard>
        {
            new Land("Swamp"),
            new Land("Island"),
            new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6),
            new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Mulligan,
            "Archon can't be cast normally and without an enabler the deck can't function");
    }

    [Fact]
    public void AdviseMulligan_ReturnsNull_AtHighMulliganDepth()
    {
        // After 3+ mulligans, defer to generic policy (return null).
        var hand = new List<ICard>
        {
            new Sorcery("Faithless Looting", manaCost: "{R}"),
            new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 3);

        decision.Should().BeNull("strategy defers to generic policy at ≥ 3 mulligans taken");
    }

    [Fact]
    public void AdviseMulligan_KeepsHand_WithThoughtScourAsEnabler()
    {
        var hand = new List<ICard>
        {
            new Land("Island"),
            new Instant("Thought Scour", manaCost: "{U}"),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Keep,
            "Thought Scour mills two and draws one — a valid opening enabler");
    }

    [Fact]
    public void AdviseMulligan_KeepsHand_WithPsychicFrogAsEnabler()
    {
        var hand = new List<ICard>
        {
            new Land("Island"),
            new Land("Swamp"),
            new Creature("Psychic Frog", manaCost: "{U}{B}", power: 1, toughness: 3),
        };

        var decision = Strategy().AdviseMulligan(hand, mulligansTaken: 0);

        decision.Should().Be(MulliganDecision.Keep,
            "Psychic Frog has a discard-a-card activated ability — it enables the graveyard plan");
    }
}
