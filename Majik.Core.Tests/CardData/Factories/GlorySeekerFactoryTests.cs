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
/// Unit tests for <see cref="GlorySeekerFactory"/> (M14 / M15, {1}{W}).
///
/// Glory Seeker is a vanilla {1}{W} Creature — Human Soldier 2/2 with no
/// printed abilities (CR 208.1 — vanilla creature).
///
/// Covers:
/// - Identity (name, mana cost, Creature type, Human + Soldier subtypes, 2/2).
/// - White colour identity via CardColors.GetColors (CR 105 — colour from pips).
/// - Mana value 2 (CR 202.3 — {1} generic + {W} pip).
/// - Owner / controller stamped on creation.
/// - No abilities attached (vanilla card).
/// - NamedCardFactory dispatch resolves the correct factory.
/// </summary>
public class GlorySeekerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GlorySeeker_Name()
    {
        var card = GlorySeekerFactory.Create(_alice);

        card.Name.Should().Be("Glory Seeker");
    }

    [Fact]
    public void GlorySeeker_ManaCost()
    {
        var card = GlorySeekerFactory.Create(_alice);

        card.ManaCost.Should().Be("{1}{W}");
    }

    [Fact]
    public void GlorySeeker_IsCreature()
    {
        var card = GlorySeekerFactory.Create(_alice);

        card.HasType(CardType.Creature).Should().BeTrue();
        card.Should().BeOfType<Creature>();
    }

    [Fact]
    public void GlorySeeker_HasHumanSubtype()
    {
        var card = GlorySeekerFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Human).Should().BeTrue(
            "Glory Seeker's oracle type line reads 'Creature — Human Soldier'");
    }

    [Fact]
    public void GlorySeeker_HasSoldierSubtype()
    {
        var card = GlorySeekerFactory.Create(_alice);

        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue(
            "Glory Seeker's oracle type line reads 'Creature — Human Soldier'");
    }

    [Fact]
    public void GlorySeeker_Power2_Toughness2()
    {
        var card = (Creature)GlorySeekerFactory.Create(_alice);

        card.BasePower.Should().Be(2,
            "Glory Seeker's printed P/T is 2/2");
        card.BaseToughness.Should().Be(2,
            "Glory Seeker's printed P/T is 2/2");
    }

    [Fact]
    public void GlorySeeker_IsWhite()
    {
        var card = GlorySeekerFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.White,
            "Glory Seeker's mana cost {1}{W} contains a white pip (CR 105)");
        CardColors.GetColors(card).Should().HaveCount(1,
            "Glory Seeker is mono-white — no other colour pips in {1}{W}");
    }

    [Fact]
    public void GlorySeeker_ManaValue2()
    {
        var card = GlorySeekerFactory.Create(_alice);

        // {1}{W} → generic 1 + one coloured pip = mana value 2 (CR 202.3).
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(2,
            "Glory Seeker's mana cost {1}{W} has mana value 2 (CR 202.3)");
    }

    [Fact]
    public void GlorySeeker_OwnerAndControllerStamped()
    {
        var card = GlorySeekerFactory.Create(_alice);

        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GlorySeeker_NoAbilities_Vanilla()
    {
        var card = GlorySeekerFactory.Create(_alice);

        card.Abilities.Should().BeEmpty(
            "Glory Seeker is a vanilla creature with no printed abilities");
    }

    [Fact]
    public void GlorySeeker_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Glory Seeker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Glory Seeker");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
    }
}
