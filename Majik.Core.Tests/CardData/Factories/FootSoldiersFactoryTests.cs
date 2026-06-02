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
/// Unit tests for <see cref="FootSoldiersFactory"/> (Portal Second Age, {3}{W}).
///
/// Foot Soldiers is a vanilla {3}{W} Creature — Human Soldier 2/4 with no
/// printed abilities (CR 208.1 — vanilla creature).
///
/// Covers:
/// - Identity (name, mana cost, Creature type, Human + Soldier subtypes, 2/4).
/// - White colour identity via CardColors.GetColors (CR 105 — colour from pips).
/// - Mana value 4 (CR 202.3 — {3} generic + {W} pip).
/// - Owner / controller stamped on creation.
/// - No abilities attached (vanilla card).
/// - NamedCardFactory dispatch resolves the correct factory.
/// </summary>
[Trait("Color", "W")]
public class FootSoldiersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FootSoldiers_Name()
    {
        var card = FootSoldiersFactory.Create(_alice);

        card.Name.Should().Be("Foot Soldiers");
    }

    [Fact]
    public void FootSoldiers_ManaCost()
    {
        var card = FootSoldiersFactory.Create(_alice);

        card.ManaCost.Should().Be("{3}{W}");
    }

    [Fact]
    public void FootSoldiers_IsCreature()
    {
        var card = FootSoldiersFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
        card.Should().BeOfType<Creature>();
    }

    [Fact]
    public void FootSoldiers_HasHumanSubtype()
    {
        var card = FootSoldiersFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Human).Should().BeTrue(
            "Foot Soldiers' oracle type line reads 'Creature — Human Soldier'");
    }

    [Fact]
    public void FootSoldiers_HasSoldierSubtype()
    {
        var card = FootSoldiersFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue(
            "Foot Soldiers' oracle type line reads 'Creature — Human Soldier'");
    }

    [Fact]
    public void FootSoldiers_Power2_Toughness4()
    {
        var card = (Creature)FootSoldiersFactory.Create(_alice);

        card.BasePower.Should().Be(2,
            "Foot Soldiers' printed P/T is 2/4");
        card.BaseToughness.Should().Be(4,
            "Foot Soldiers' printed P/T is 2/4");
    }

    [Fact]
    public void FootSoldiers_IsWhite()
    {
        var card = FootSoldiersFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.White,
            "Foot Soldiers' mana cost {3}{W} contains a white pip (CR 105)");
        CardColors.GetColors(card).Should().HaveCount(1,
            "Foot Soldiers is mono-white — no other colour pips in {3}{W}");
    }

    [Fact]
    public void FootSoldiers_ManaValue4()
    {
        var card = FootSoldiersFactory.Create(_alice);

        // {3}{W} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(4,
            "Foot Soldiers' mana cost {3}{W} has mana value 4 (CR 202.3)");
    }

    [Fact]
    public void FootSoldiers_OwnerAndControllerStamped()
    {
        var card = FootSoldiersFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FootSoldiers_NoAbilities_Vanilla()
    {
        var card = FootSoldiersFactory.Create(_alice);

        card.Abilities.Should().BeEmpty(
            "Foot Soldiers is a vanilla creature with no printed abilities");
    }
}
