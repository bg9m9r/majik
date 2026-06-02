using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BogImpFactory"/> (The Dark, {1}{B}).
///
/// Covers:
/// - Identity ({1}{B} Creature — Imp 1/1, mana value 2, black).
/// - Flying keyword marker (CR 702.9).
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "B")]
public class BogImpFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BogImp_Name_IsBogImp()
    {
        var imp = BogImpFactory.Create(_alice);

        imp.Name.Should().Be("Bog Imp");
    }

    [Fact]
    public void BogImp_ManaCost_IsOneBBlack()
    {
        var imp = BogImpFactory.Create(_alice);

        imp.ManaCost.ToString().Should().Be("{1}{B}");
    }

    [Fact]
    public void BogImp_ManaValue_IsTwo()
    {
        var imp = BogImpFactory.Create(_alice);

        imp.ManaCostValue.TotalValue.Should().Be(2,
            "mana value of {1}{B} is 2 (CR 202.3)");
    }

    [Fact]
    public void BogImp_PowerAndToughness_IsOneOne()
    {
        var imp = BogImpFactory.Create(_alice);

        imp.BasePower.Should().Be(1);
        imp.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void BogImp_IsCreatureWithImpSubtype()
    {
        var imp = BogImpFactory.Create(_alice);

        imp.HasType(CardType.Creature).Should().BeTrue();
        imp.HasSubtype(CardSubtype.Imp).Should().BeTrue();
    }

    [Fact]
    public void BogImp_IsBlack()
    {
        var imp = BogImpFactory.Create(_alice);

        CardColors.GetColors(imp).Should().Contain(ManaColor.Black,
            "{1}{B} is a black card (CR 202.2)");
    }

    [Fact]
    public void BogImp_OwnerAndController_AreSetToOwner()
    {
        var imp = BogImpFactory.Create(_alice);

        imp.Owner.Should().BeSameAs(_alice);
        imp.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Flying keyword marker — CR 702.9
    // -----------------------------------------------------------------------

    [Fact]
    public void BogImp_HasFlyingKeyword()
    {
        var imp = BogImpFactory.Create(_alice);

        var keywords = imp.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying",
            "Bog Imp has Flying (CR 702.9)");
    }

    // -----------------------------------------------------------------------
    // Named-card dispatch
    // -----------------------------------------------------------------------
}
