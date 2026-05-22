using FluentAssertions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class BespokeTemplateTests
{
    private static SpellBindContext Ctx(string text) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, null, null);

    [Fact]
    public void ThoughtseizePatternTemplate_MatchesThoughtseizeOracle()
    {
        new ThoughtseizePatternTemplate().TryBind(
            Ctx("Target player reveals their hand. You choose a nonland card from it. That player discards that card. You lose 2 life."))
            .Should().NotBeNull();
    }

    [Fact]
    public void MalevolentRumblePatternTemplate_MatchesMalevolentRumbleOracle()
    {
        new MalevolentRumblePatternTemplate().TryBind(
            Ctx("Reveal the top four cards of your library. You may put a permanent card from among them into your hand. Put the rest into your graveyard. Create a 0/1 colorless Eldrazi Spawn creature token with \"Sacrifice this creature: Add {C}.\""))
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it. That player discards that card.")]
    [InlineData("Target opponent reveals their hand. You choose a card from it. That player discards that card.")]
    [InlineData("Target opponent reveals their hand. You choose a noncreature, nonland card from it. That player discards that card.")]
    [InlineData("Target opponent reveals their hand. You choose a creature or planeswalker card from it. That player discards that card.")]
    [InlineData("Target opponent reveals their hand. You choose an artifact card from it. That player discards that card.")]
    [InlineData("Target opponent reveals their hand. You choose a nonlegendary, nonland card from it. That player discards that card.")]
    public void RevealHandThenDiscardTemplate_MatchesDuressFamily(string oracle)
    {
        new RevealHandThenDiscardTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Trailing rider clauses (lossy v1 — rider ignored at resolution but bind still succeeds)
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it. That player discards that card. If you control a Warrior, that player loses 2 life.")]
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it. That player discards that card. Put a +1/+1 counter on a creature you control.")]
    public void RevealHandThenDiscardTemplate_AcceptsTrailingRiders(string oracle)
    {
        new RevealHandThenDiscardTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Should NOT match: exile resolution (Duress family is discard, not exile)
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it and exile that card.")]
    // Should NOT match: graveyard-or-hand source (separate template)
    [InlineData("Target opponent reveals their hand. You choose a nonland card from that player's graveyard or hand and exile it.")]
    public void RevealHandThenDiscardTemplate_DoesNotMatchOutOfFamily(string oracle)
    {
        new RevealHandThenDiscardTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }

    [Theory]
    // "Target player" variant now binds — Distress, Inquisition of Kozilek.
    // (Thoughtseize itself still binds via ThoughtseizePatternTemplate which
    // has higher priority in the live registry.)
    [InlineData("Target player reveals their hand. You choose a nonland card from it. That player discards that card.")]
    [InlineData("Target player reveals their hand. You choose a nonland card from it with mana value 3 or less. That player discards that card.")]
    public void RevealHandThenDiscardTemplate_AcceptsTargetPlayerVariant(string oracle)
    {
        new RevealHandThenDiscardTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it and exile that card.")]
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it. Exile that card.")]
    [InlineData("Target opponent reveals their hand. You choose an artifact or creature card from it. Exile that card.")]
    [InlineData("Target opponent reveals their hand. You choose a card from it with mana value 4 or greater and exile that card.")]
    public void RevealHandThenExileTemplate_MatchesCastigateFamily(string oracle)
    {
        new RevealHandThenExileTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Trailing rider clauses — bind succeeds, rider dropped at resolution.
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it and exile that card. Put a +1/+1 counter on up to one target creature you control.")]
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it. Exile that card. If the card's mana value is 1 or less, create a 1/1 white and black Spirit creature token with flying.")]
    public void RevealHandThenExileTemplate_AcceptsTrailingRiders(string oracle)
    {
        new RevealHandThenExileTemplate().TryBind(Ctx(oracle))
            .Should().NotBeNull();
    }

    [Theory]
    // Should NOT match: discard variant — RevealHandThenDiscard owns that shape.
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it. That player discards that card.")]
    // Should NOT match: graveyard-alt source (Agonizing Remorse / Psychic Intrusion etc).
    [InlineData("Target opponent reveals their hand. You choose a nonland card from that player's graveyard or hand and exile it.")]
    public void RevealHandThenExileTemplate_DoesNotMatchOutOfFamily(string oracle)
    {
        new RevealHandThenExileTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }

    [Fact]
    public void RevealUntilNonlandDamageTemplate_MatchesCalibratedBlastOracle()
    {
        new RevealUntilNonlandDamageTemplate().TryBind(
            Ctx("Reveal cards from the top of your library until you reveal a nonland card. Put the revealed cards on the bottom of your library in a random order. When you reveal a nonland card this way, Calibrated Blast deals damage equal to that card's mana value to any target."))
            .Should().NotBeNull();
    }

    [Fact]
    public void RevealUntilArtifactToBattlefieldTemplate_MatchesMadcapExperimentOracle()
    {
        new RevealUntilArtifactToBattlefieldTemplate().TryBind(
            Ctx("Reveal cards from the top of your library until you reveal an artifact card. Put that card onto the battlefield and the rest on the bottom of your library in a random order. Madcap Experiment deals damage to you equal to the number of cards revealed this way."))
            .Should().NotBeNull();
    }

    [Fact]
    public void RevealUntilLandToBattlefieldTemplate_MatchesRecrossThePathsOracle()
    {
        new RevealUntilLandToBattlefieldTemplate().TryBind(
            Ctx("Reveal cards from the top of your library until you reveal a land card. Put that card onto the battlefield and the rest on the bottom of your library in any order. Clash with an opponent. If you win, return Recross the Paths to its owner's hand."))
            .Should().NotBeNull();
    }

    [Fact]
    public void RevealUntilNonlandToHandTemplate_MatchesTreasureHuntOracle()
    {
        new RevealUntilNonlandToHandTemplate().TryBind(
            Ctx("Reveal cards from the top of your library until you reveal a nonland card, then put all cards revealed this way into your hand."))
            .Should().NotBeNull();
    }

    [Fact]
    public void RevealUntilFamily_DoesNotCrossMatch()
    {
        var treasure = "Reveal cards from the top of your library until you reveal a nonland card, then put all cards revealed this way into your hand.";
        var calibrated = "Reveal cards from the top of your library until you reveal a nonland card. Put the revealed cards on the bottom of your library in a random order. When you reveal a nonland card this way, Calibrated Blast deals damage equal to that card's mana value to any target.";

        // Treasure Hunt should not match the Calibrated Blast template, and vice versa.
        new RevealUntilNonlandToHandTemplate().TryBind(Ctx(calibrated)).Should().BeNull();
        new RevealUntilNonlandDamageTemplate().TryBind(Ctx(treasure)).Should().BeNull();
    }
}
