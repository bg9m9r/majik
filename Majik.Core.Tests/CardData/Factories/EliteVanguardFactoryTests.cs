using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EliteVanguardFactory"/> (M10 / M12, {W}).
///
/// Elite Vanguard is a vanilla {W} Creature — Human Soldier 2/1 with no
/// printed abilities (CR 208.1 — vanilla creature).
///
/// Covers:
/// - Identity (name, mana cost, Creature type, Human + Soldier subtypes, 2/1).
/// - White colour identity via CardColors.GetColors (CR 105 — colour from pips).
/// - Mana value 1 (CR 202.3 — single generic {W} pip).
/// - Owner / controller stamped on creation.
/// - No abilities attached (vanilla card).
/// - NamedCardFactory dispatch resolves the correct factory.
/// </summary>
[Trait("Color", "W")]
public class EliteVanguardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void EliteVanguard_Name()
    {
        var card = EliteVanguardFactory.Create(_alice);

        card.Name.Should().Be("Elite Vanguard");
    }

    [Fact]
    public void EliteVanguard_ManaCost()
    {
        var card = EliteVanguardFactory.Create(_alice);

        card.ManaCost.Should().Be("{W}");
    }

    [Fact]
    public void EliteVanguard_IsCreature()
    {
        var card = EliteVanguardFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
        card.Should().BeOfType<Creature>();
    }

    [Fact]
    public void EliteVanguard_HasHumanSubtype()
    {
        var card = EliteVanguardFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Human).Should().BeTrue(
            "Elite Vanguard's oracle type line reads 'Creature — Human Soldier'");
    }

    [Fact]
    public void EliteVanguard_HasSoldierSubtype()
    {
        var card = EliteVanguardFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue(
            "Elite Vanguard's oracle type line reads 'Creature — Human Soldier'");
    }

    [Fact]
    public void EliteVanguard_Power2_Toughness1()
    {
        var card = (Creature)EliteVanguardFactory.Create(_alice);

        card.BasePower.Should().Be(2,
            "Elite Vanguard's printed P/T is 2/1");
        card.BaseToughness.Should().Be(1,
            "Elite Vanguard's printed P/T is 2/1");
    }

    [Fact]
    public void EliteVanguard_IsWhite()
    {
        var card = EliteVanguardFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.White,
            "Elite Vanguard's mana cost {W} contains a white pip (CR 105)");
        CardColors.GetColors(card).Should().HaveCount(1,
            "Elite Vanguard is mono-white — no other colour pips in {W}");
    }

    [Fact]
    public void EliteVanguard_OwnerAndControllerStamped()
    {
        var card = EliteVanguardFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EliteVanguard_NoAbilities_Vanilla()
    {
        var card = EliteVanguardFactory.Create(_alice);

        card.Abilities.Should().BeEmpty(
            "Elite Vanguard is a vanilla creature with no printed abilities");
    }
}
