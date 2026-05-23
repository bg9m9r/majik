using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 613 — backcompat + widening coverage for the
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> overload.
/// </summary>
public class PermanentLayerComputeTests
{
    [Fact]
    public void Compute_OnLand_SeedsPrintedTypesAndSubtypes()
    {
        var svc = new ContinuousEffectsService();
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });

        var chars = svc.Compute((Permanent)forest);

        chars.Types.Should().Contain(CardType.Land);
        chars.Subtypes.Should().Contain(CardSubtype.Forest);
    }

    [Fact]
    public void Compute_OnCreature_PreservesPower_ViaPermanentOverload()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2);

        var chars = svc.Compute((Permanent)bear);

        chars.Should().BeOfType<CreatureCharacteristics>();
        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(2);
        cc.Toughness.Should().Be(2);
        cc.Types.Should().Contain(CardType.Creature);
    }

    [Fact]
    public void Compute_OnArtifact_SeedsPrintedTypes()
    {
        var svc = new ContinuousEffectsService();
        var artifact = new Artifact("Sol Ring", "1");

        var chars = svc.Compute((Permanent)artifact);

        chars.Types.Should().Contain(CardType.Artifact);
        chars.Should().NotBeOfType<CreatureCharacteristics>();
    }
}
