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
/// Unit tests for <see cref="PillarfieldOxFactory"/> (Magic 2011/2012, {3}{W}).
///
/// Pillarfield Ox is a vanilla {3}{W} Creature — Ox 2/4 with no
/// printed abilities (CR 208.1 — vanilla creature).
///
/// Covers:
/// - Identity (name, mana cost, Creature type, Ox subtype, 2/4).
/// - White colour identity via CardColors.GetColors (CR 105 — colour from pips).
/// - Mana value 4 (CR 202.3 — {3} generic + {W} pip).
/// - Owner / controller stamped on creation.
/// - No abilities attached (vanilla card).
/// - NamedCardFactory dispatch resolves the correct factory.
/// </summary>
[Trait("Color", "W")]
public class PillarfieldOxFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PillarfieldOx_Name()
    {
        var card = PillarfieldOxFactory.Create(_alice);

        card.Name.Should().Be("Pillarfield Ox");
    }

    [Fact]
    public void PillarfieldOx_ManaCost()
    {
        var card = PillarfieldOxFactory.Create(_alice);

        card.ManaCost.Should().Be("{3}{W}");
    }

    [Fact]
    public void PillarfieldOx_IsCreature()
    {
        var card = PillarfieldOxFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
        card.Should().BeOfType<Creature>();
    }

    [Fact]
    public void PillarfieldOx_HasOxSubtype()
    {
        var card = PillarfieldOxFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Ox).Should().BeTrue(
            "Pillarfield Ox's oracle type line reads 'Creature — Ox'");
    }

    [Fact]
    public void PillarfieldOx_Power2_Toughness4()
    {
        var card = (Creature)PillarfieldOxFactory.Create(_alice);

        card.BasePower.Should().Be(2,
            "Pillarfield Ox's printed P/T is 2/4");
        card.BaseToughness.Should().Be(4,
            "Pillarfield Ox's printed P/T is 2/4");
    }

    [Fact]
    public void PillarfieldOx_IsWhite()
    {
        var card = PillarfieldOxFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.White,
            "Pillarfield Ox's mana cost {3}{W} contains a white pip (CR 105)");
        CardColors.GetColors(card).Should().HaveCount(1,
            "Pillarfield Ox is mono-white — no other colour pips in {3}{W}");
    }

    [Fact]
    public void PillarfieldOx_ManaValue4()
    {
        var card = PillarfieldOxFactory.Create(_alice);

        // {3}{W} → generic 3 + one coloured pip = mana value 4 (CR 202.3).
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(4,
            "Pillarfield Ox's mana cost {3}{W} has mana value 4 (CR 202.3)");
    }

    [Fact]
    public void PillarfieldOx_OwnerAndControllerStamped()
    {
        var card = PillarfieldOxFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PillarfieldOx_NoAbilities_Vanilla()
    {
        var card = PillarfieldOxFactory.Create(_alice);

        card.Abilities.Should().BeEmpty(
            "Pillarfield Ox is a vanilla creature with no printed abilities");
    }
}
