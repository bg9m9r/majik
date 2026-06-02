using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FangrenHunterFactory"/> (Darksteel, {3}{G}{G}).
///
/// Creature — Beast 4/4. Oracle text:
///   "Trample"
///
/// Covers:
///   - Identity (Beast 4/4 at {3}{G}{G}, mana value 5, green).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trample keyword wired; <see cref="CombatAbilities.HasTrample"/> reads it.
///   - No other abilities beyond the single Trample keyword.
/// </summary>
[Trait("Color", "G")]
public class FangrenHunterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeCostPowerToughness()
    {
        var card = FangrenHunterFactory.Create(_alice);

        card.Name.Should().Be("Fangren Hunter");
        card.ManaCost.Should().Be("{3}{G}{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.Power.Should().Be(4);
        creature.Toughness.Should().Be(4);
    }

    [Fact]
    public void Identity_ManaValue_IsFive()
    {
        var card = FangrenHunterFactory.Create(_alice);

        // {3}{G}{G} = 3 + 1 + 1 = 5 (CR 202.3)
        card.ManaCostValue.TotalValue.Should().Be(5);
    }
    [Fact]
    public void Card_HasTrampleKeyword()
    {
        var card = FangrenHunterFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Trample",
                "Fangren Hunter has printed Trample (CR 702.19)");
    }

    [Fact]
    public void CombatAbilities_HasTrample_ReturnsTrue()
    {
        var card = FangrenHunterFactory.Create(_alice);

        CombatAbilities.HasTrample(card).Should().BeTrue(
            "Trample keyword marker must be recognized by CombatAbilities");
    }

    [Fact]
    public void Card_HasNoOtherAbilities()
    {
        var card = FangrenHunterFactory.Create(_alice);

        // Exactly one ability: the Trample KeywordAbility.
        card.Abilities.Should().HaveCount(1,
            "Fangren Hunter is a vanilla trample creature — one keyword, no other abilities");
        card.Abilities.Single().Should().BeOfType<KeywordAbility>();
    }
}
