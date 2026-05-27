using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DuskImpFactory"/> (Portal, {2}{B}).
///
/// Covers:
/// - Card identity (name, mana cost {2}{B}, 2/1, Creature — Imp,
///   owner/controller).
/// - Black colour (CR 105 — single B pip).
/// - Mana value 3 ({2}{B} = generic 2 + 1 black = 3).
/// - Flying keyword marker (CR 702.9).
/// - NamedCardFactory dispatch.
/// </summary>
public class DuskImpFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DuskImp_Is_Imp_2_1_At_2B()
    {
        var imp = DuskImpFactory.Create(_alice);

        imp.Name.Should().Be("Dusk Imp");
        imp.ManaCost.Should().Be("{2}{B}");
        imp.BasePower.Should().Be(2);
        imp.BaseToughness.Should().Be(1);
        imp.HasType(CardType.Creature).Should().BeTrue();
        imp.HasSubtype(CardSubtype.Imp).Should().BeTrue();
        imp.Owner.Should().BeSameAs(_alice);
        imp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DuskImp_IsBlack()
    {
        var imp = DuskImpFactory.Create(_alice);

        var colors = CardColors.GetColors(imp);
        colors.Should().ContainSingle()
            .Which.Should().Be(ManaColor.Black,
                "CR 105 — {2}{B} has a single black pip, no other colours");
    }

    [Fact]
    public void DuskImp_ManaValueIsThree()
    {
        var imp = DuskImpFactory.Create(_alice);

        imp.ManaCostValue.TotalValue.Should().Be(3,
            because: "{2}{B} = generic 2 + 1 black = mana value 3");
    }

    [Fact]
    public void DuskImp_HasFlyingKeyword()
    {
        var imp = DuskImpFactory.Create(_alice);

        imp.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Dusk Imp has Flying");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DuskImp()
    {
        var card = NamedCardFactory.Create("Dusk Imp", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Dusk Imp");
        card.HasSubtype(CardSubtype.Imp).Should().BeTrue();
    }
}
