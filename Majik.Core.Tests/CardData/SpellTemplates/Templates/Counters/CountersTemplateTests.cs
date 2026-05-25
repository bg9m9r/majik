using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Counters;

public class CountersTemplateTests
{
    private static SpellBindContext Ctx(string text, ContinuousEffectsService? effects = null) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, effects, null);

    [Fact]
    public void PutPlusCounterTemplate_MatchesPutPlusOnePlusOneCounter()
    {
        new PutPlusCounterTemplate()
            .TryBind(Ctx("Put a +1/+1 counter on target creature."))
            .Should().NotBeNull();
    }

    [Fact]
    public void PutMinusCounterTemplate_MatchesPutMinusOneMinusOneCounter()
    {
        new PutMinusCounterTemplate()
            .TryBind(Ctx("Put a -1/-1 counter on target creature."))
            .Should().NotBeNull();
    }

    [Fact]
    public void CreaturesGetPlusCounterTemplate_MatchesEachCreatureYouControlGetsCounter()
    {
        new CreaturesGetPlusCounterTemplate()
            .TryBind(Ctx("Each creature you control gets a +1/+1 counter on it."))
            .Should().NotBeNull();
    }

    [Fact]
    public void PumpCreatureTemplate_MatchesTargetCreatureGetsPump()
    {
        new PumpCreatureTemplate()
            .TryBind(Ctx("Target creature gets +2/+2 until end of turn."))
            .Should().NotBeNull();
    }

    [Fact]
    public void GrantKeywordTilEotTemplate_MatchesTargetCreatureGainsFlying()
    {
        new GrantKeywordTilEotTemplate()
            .TryBind(Ctx("Target creature gains flying until end of turn."))
            .Should().NotBeNull();
    }

    [Fact]
    public void CreaturesYouControlPumpTemplate_ReturnsNonNull_WhenEffectsProvided()
    {
        new CreaturesYouControlPumpTemplate()
            .TryBind(Ctx("Creatures you control get +1/+1 until end of turn.", new ContinuousEffectsService()))
            .Should().NotBeNull();
    }

    [Fact]
    public void CreaturesYouControlPumpTemplate_ReturnsNull_WhenEffectsIsNull()
    {
        new CreaturesYouControlPumpTemplate()
            .TryBind(Ctx("Creatures you control get +1/+1 until end of turn.", effects: null))
            .Should().BeNull();
    }

    [Fact]
    public void CreaturesYouControlGainKeywordTemplate_ReturnsNonNull_WhenEffectsProvided()
    {
        new CreaturesYouControlGainKeywordTemplate()
            .TryBind(Ctx("Creatures you control gain trample until end of turn.", new ContinuousEffectsService()))
            .Should().NotBeNull();
    }

    [Fact]
    public void CreaturesYouControlGainKeywordTemplate_ReturnsNull_WhenEffectsIsNull()
    {
        new CreaturesYouControlGainKeywordTemplate()
            .TryBind(Ctx("Creatures you control gain trample until end of turn.", effects: null))
            .Should().BeNull();
    }
}
