using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 700.2 — how many modes a modal spell lets its caster choose, and in
/// which order the chosen modes resolve (CR 608.2c).
/// </summary>
public class ModalModeBoundsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Theory]
    [InlineData("one", 4, 1, 1)]
    [InlineData("two", 4, 2, 2)]
    [InlineData("three", 5, 3, 3)]
    [InlineData("one or both", 2, 1, 2)]
    [InlineData("one or more", 3, 1, 3)]
    [InlineData("one or more", 5, 1, 5)]
    public void ThePickWord_SetsMinAndMax(string pick, int modeCount, int min, int max) =>
        ModalChooseOneTemplate.ModeBounds(pick, modeCount).Should().Be((min, max));

    [Fact]
    public void ChosenModes_AreDistinct_Capped_AndInPrintedOrder() =>
        ChosenModes.InPrintedOrder(new[] { 3, 1, 3, 7, -1, 0 }, modeCount: 4, maxModes: 2)
            .Should().Equal(1, 3);

    [Fact]
    public void KolaghansCommand_ChoosesExactlyTwo()
    {
        var def = KolaghansCommandFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob });
        (def.MinModes, def.MaxModes).Should().Be((2, 2));
    }

    [Fact]
    public void AtarkasCommand_ChoosesExactlyTwo()
    {
        var def = AtarkasCommandFactory.BuildDefinition(_alice, new[] { _alice, _bob });
        (def.MinModes, def.MaxModes).Should().Be((2, 2));
    }

    [Fact]
    public void KayasGuile_ChoosesTwo_OrAllWhenEntwined()
    {
        var def = KayasGuileFactory.BuildDefinition(_alice, new[] { _alice, _bob }, agent: null);
        (def.MinModes, def.MaxModes).Should().Be((2, 2));
        var entwined = KayasGuileFactory.BuildDefinition(_alice, new[] { _alice, _bob }, agent: null, entwined: true);
        (entwined.MinModes, entwined.MaxModes).Should().Be((4, 4));
    }

    [Fact]
    public async Task TheHeuristicBot_AnswersAMultiModePrompt_Legally()
    {
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack());
        var modes = new[] { "Draw a card", "Destroy target creature", "You gain 4 life", "Tap all creatures" };

        var two = await bot.ChooseModesAsync(ctx, modes, 2, 2);
        two.Should().HaveCount(2).And.OnlyHaveUniqueItems().And.OnlyContain(i => i >= 0 && i < 4);

        var single = await bot.ChooseModeAsync(ctx, modes);
        var oneOrMore = await bot.ChooseModesAsync(ctx, modes, 1, 4);
        oneOrMore.Should().Equal(new[] { single }, "the bot keeps its single best pick");
    }
}
