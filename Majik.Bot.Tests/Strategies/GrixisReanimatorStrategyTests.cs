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

    // ── TryGetNextWinningAction ─────────────────────────────────────────────

    [Fact]
    public void TryGetNextWinningAction_ReturnsReanimate_WhenEmperorAndArchonInYard_PersistInHand_WithMana()
    {
        // Arrange: Archon of Cruelty + Emperor of Bones in graveyard,
        // Persist in hand, 3 lands (CMC of Persist = {2}{B} = 3).
        var s = new BotTestScenario();

        // Graveyard: Emperor of Bones (CMC 2 — valid Persist target).
        var emperor = new Creature("Emperor of Bones", manaCost: "{1}{B}", power: 2, toughness: 2);
        emperor.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(emperor);

        // Graveyard: Archon of Cruelty (the payoff — reason we care).
        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        // Hand: Persist — the reanimate spell.
        s.AddCardToHand(s.Self, new Sorcery("Persist", manaCost: "{2}{B}"));

        // Lands: 3 untapped sources (Persist needs CMC 3).
        s.AddLandToBattlefield(s.Self, "Swamp1");
        s.AddLandToBattlefield(s.Self, "Swamp2");
        s.AddLandToBattlefield(s.Self, "Swamp3");

        // Act
        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        // Assert: we get a CastSpell for Persist, with Emperor as the target.
        action.Should().BeOfType<PriorityAction.CastSpell>("the line is assembled — should cast Persist");
        var cast = (PriorityAction.CastSpell)action!;
        cast.Card.Name.Should().Be("Persist");
        cast.Targets.Should().ContainSingle(t => ReferenceEquals(t, emperor),
            "Emperor of Bones in the graveyard should be passed as the explicit target");
    }

    [Fact]
    public void TryGetNextWinningAction_ReturnsReanimate_WithUnearth_WhenPersistAbsent()
    {
        // Arrange: Archon + Emperor in yard, only Unearth in hand, 1 land
        // (Unearth costs {B} = CMC 1).
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

        action.Should().BeOfType<PriorityAction.CastSpell>();
        ((PriorityAction.CastSpell)action!).Card.Name.Should().Be("Unearth");
    }

    [Fact]
    public void TryGetNextWinningAction_ReturnsNull_WhenArchonInYard_ButEmperorNotInYard()
    {
        // Win-line 1 requires both Archon AND Emperor in the graveyard;
        // without Emperor there's no valid CMC-≤3 target for Persist.
        var s = new BotTestScenario();

        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        s.AddCardToHand(s.Self, new Sorcery("Persist", manaCost: "{2}{B}"));
        s.AddLandToBattlefield(s.Self, "Swamp1");
        s.AddLandToBattlefield(s.Self, "Swamp2");
        s.AddLandToBattlefield(s.Self, "Swamp3");

        // Win-line 1 aborts — no Emperor. Win-line 2 checks if Archon is in
        // yard (it is), so it also skips FaithlessLooting. Net result: null.
        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("Emperor is not in the graveyard — reanimate line is incomplete");
    }

    [Fact]
    public void TryGetNextWinningAction_ReturnsNull_WhenReanimateSpellNotInHand()
    {
        // Pieces in yard but no reanimate spell — line not executable.
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
        // No Persist or Unearth in hand.

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("no reanimation spell in hand");
    }

    [Fact]
    public void TryGetNextWinningAction_ReturnsNull_WhenInsufficientMana()
    {
        // All pieces present but not enough mana to cast Persist ({2}{B} = CMC 3).
        var s = new BotTestScenario();

        var emperor = new Creature("Emperor of Bones", manaCost: "{1}{B}", power: 2, toughness: 2);
        emperor.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(emperor);

        var archon = new Creature("Archon of Cruelty", manaCost: "{6}{B}{B}", power: 6, toughness: 6);
        archon.ChangeOwner(s.Self);
        s.Self.Zones.Graveyard.AddCard(archon);

        s.AddCardToHand(s.Self, new Sorcery("Persist", manaCost: "{2}{B}"));
        // Only 2 lands — CMC 3 is unaffordable.
        s.AddLandToBattlefield(s.Self, "Swamp1");
        s.AddLandToBattlefield(s.Self, "Swamp2");

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeNull("insufficient mana to cast Persist");
    }

    [Fact]
    public void TryGetNextWinningAction_ReturnsFaithlessLooting_WhenArchonNotYetInYard()
    {
        // Archon not yet in graveyard → win-line 2: cast Faithless Looting
        // to bin Archon (and set up the full chain next turn).
        var s = new BotTestScenario();

        // Faithless Looting at {R} = CMC 1.
        s.AddCardToHand(s.Self, new Sorcery("Faithless Looting", manaCost: "{R}"));
        s.AddLandToBattlefield(s.Self, "Mountain");

        var action = Strategy().TryGetNextWinningAction(s.Context, s.Self);

        action.Should().BeOfType<PriorityAction.CastSpell>();
        ((PriorityAction.CastSpell)action!).Card.Name.Should().Be("Faithless Looting",
            "Looting is the cheapest enabler to start filling the graveyard");
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
