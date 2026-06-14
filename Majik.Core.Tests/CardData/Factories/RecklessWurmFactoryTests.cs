using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RecklessWurmFactory"/> (Torment, {3}{R}{R}).
///
/// Reckless Wurm's BODY is a vanilla 4/4 Wurm with Trample (CR 702.19); its
/// only other printed line is the madness keyword. We assert identity, that
/// Trample is honoured by the combat layer, and that the card is catalogued for
/// intrinsic madness (CR 702.35).
/// </summary>
[Trait("Color", "R")]
public class RecklessWurmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RecklessWurm_Identity()
    {
        var card = RecklessWurmFactory.Create(_alice);

        card.Name.Should().Be("Reckless Wurm");
        card.ManaCost.Should().Be("{3}{R}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(4);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RecklessWurm_HasTrample()
    {
        var card = RecklessWurmFactory.Create(_alice);

        CombatAbilities.HasTrample((Permanent)card).Should().BeTrue(
            "Reckless Wurm has Trample (CR 702.19)");
    }

    [Fact]
    public void RecklessWurm_IsCataloguedForMadness()
    {
        var card = RecklessWurmFactory.Create(_alice);

        MadnessCatalog.HasMadness(card).Should().BeTrue(
            "madness {2}{R} is intrinsic via MadnessCatalog (CR 702.35)");
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{2}{R}"));
    }
}
