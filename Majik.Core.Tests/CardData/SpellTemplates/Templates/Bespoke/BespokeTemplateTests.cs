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
    // Should NOT match: original Thoughtseize wording ("Target player", trailing life loss)
    [InlineData("Target player reveals their hand. You choose a nonland card from it. That player discards that card. You lose 2 life.")]
    // Should NOT match: exile resolution (Duress family is discard, not exile)
    [InlineData("Target opponent reveals their hand. You choose a nonland card from it and exile that card.")]
    public void RevealHandThenDiscardTemplate_DoesNotMatchOutOfFamily(string oracle)
    {
        new RevealHandThenDiscardTemplate().TryBind(Ctx(oracle))
            .Should().BeNull();
    }
}
