using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AncientBrontodonFactory"/> (Ixalan, {6}{G}{G}).
///
/// Covers:
/// - Identity: Creature — Dinosaur, mana cost {6}{G}{G}, P/T 9/9,
///   owner / controller, green via CardColors.GetColors, mana value 8.
/// - No abilities (vanilla).
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class AncientBrontodonFactoryTests
{
    private readonly Majik.Core.Players.Player _alice = new("Alice", 20);

    [Fact]
    public void AncientBrontodon_Identity()
    {
        var card = AncientBrontodonFactory.Create(_alice);

        card.Name.Should().Be("Ancient Brontodon");
        card.ManaCost.Should().Be("{6}{G}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue(
            "Ancient Brontodon is a Dinosaur (CR 205.3m)");
        card.BasePower.Should().Be(9);
        card.BaseToughness.Should().Be(9);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AncientBrontodon_IsGreen()
    {
        var card = AncientBrontodonFactory.Create(_alice);

        CardColors.GetColors(card).Should().ContainSingle(
            "Ancient Brontodon's cost {6}{G}{G} has two green pips (CR 105)");
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void AncientBrontodon_ManaValue_IsEight()
    {
        var card = AncientBrontodonFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(8,
            "6 generic + 2 green = mana value 8 (CR 202.3)");
    }

    [Fact]
    public void AncientBrontodon_IsVanilla_NoAbilities()
    {
        var card = AncientBrontodonFactory.Create(_alice);

        card.Abilities.Should().BeEmpty(
            "Ancient Brontodon is a vanilla creature with no printed abilities");
    }

    [Fact]
    public void AncientBrontodon_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Ancient Brontodon", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ancient Brontodon");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(9);
        ((Creature)card).BaseToughness.Should().Be(9);
    }
}
