using FluentAssertions;
using Majik.Core.Targeting;
using Xunit;

namespace Majik.Core.Tests.TargetingPipeline;

public class TargetCandidateServiceClassifyTests
{
    [Theory]
    [InlineData("any target", TargetCategory.AnyTarget)]
    [InlineData("target creature", TargetCategory.Creature)]
    [InlineData("target player", TargetCategory.Player)]
    [InlineData("target opponent", TargetCategory.Opponent)]
    [InlineData("target creature or player", TargetCategory.CreatureOrPlayer)]
    [InlineData("target player or planeswalker", TargetCategory.PlayerOrPlaneswalker)]
    [InlineData("target creature or planeswalker", TargetCategory.CreatureOrPlaneswalker)]
    [InlineData("target planeswalker", TargetCategory.Planeswalker)]
    [InlineData("target nonland permanent", TargetCategory.NonlandPermanent)]
    [InlineData("target permanent", TargetCategory.Permanent)]
    [InlineData("target spell", TargetCategory.Spell)]
    [InlineData("target noncreature spell", TargetCategory.NoncreatureSpell)]
    [InlineData("target creature spell", TargetCategory.CreatureSpell)]
    [InlineData("target card in a graveyard", TargetCategory.GraveyardCard)]
    [InlineData("target artifact", TargetCategory.Artifact)]
    [InlineData("target enchantment", TargetCategory.Enchantment)]
    [InlineData("target land", TargetCategory.Land)]
    [InlineData("target creature with power 1 or less", TargetCategory.Creature)]
    [InlineData("no target", TargetCategory.None)]
    [InlineData("", TargetCategory.None)]
    public void Classify_maps_description_to_category(string desc, TargetCategory expected)
    {
        TargetCandidateService.Classify(desc).Should().Be(expected);
    }
}
