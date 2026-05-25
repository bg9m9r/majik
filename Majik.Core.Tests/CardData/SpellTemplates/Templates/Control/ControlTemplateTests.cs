using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Control;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Control;

public class ControlTemplateTests
{
    private static SpellBindContext Ctx(string text, ContinuousEffectsService? effects = null) =>
        new(new CardEntity { Name = "X", OracleText = text },
            new Player("A", 20), _ => _, effects, null);

    [Fact]
    public void TapTargetTemplate_MatchesTapTargetPermanent()
    {
        new TapTargetTemplate()
            .TryBind(Ctx("Tap target permanent."))
            .Should().NotBeNull();
    }

    [Fact]
    public void UntapTargetTemplate_MatchesUntapTargetCreature()
    {
        new UntapTargetTemplate()
            .TryBind(Ctx("Untap target creature."))
            .Should().NotBeNull();
    }

    [Fact]
    public void BounceTargetTemplate_MatchesReturnTargetCreatureToOwnersHand()
    {
        new BounceTargetTemplate()
            .TryBind(Ctx("Return target creature to its owner's hand."))
            .Should().NotBeNull();
    }

    [Fact]
    public void ExileTargetTemplate_MatchesExileTargetCreature()
    {
        new ExileTargetTemplate()
            .TryBind(Ctx("Exile target creature."))
            .Should().NotBeNull();
    }

    [Fact]
    public void GainControlTemplate_MatchesGainControlOfTargetCreature_WhenEffectsProvided()
    {
        new GainControlTemplate()
            .TryBind(Ctx("Gain control of target creature.", new ContinuousEffectsService()))
            .Should().NotBeNull();
    }

    [Fact]
    public void GainControlTemplate_ReturnsNull_WhenEffectsIsNull()
    {
        new GainControlTemplate()
            .TryBind(Ctx("Gain control of target creature.", effects: null))
            .Should().BeNull();
    }
}
