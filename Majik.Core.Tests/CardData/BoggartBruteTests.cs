using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BoggartBruteFactory"/> (Magic Origins, {2}{R}).
///
/// Creature — Goblin Warrior 3/2. Oracle text:
///   "Menace (This creature can't be blocked except by two or more creatures.)"
///
/// Covers:
///   - Identity (Creature — Goblin Warrior, {2}{R}, 3/2, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Printed Menace keyword marker (CR 702.110).
///   - Color: red (CardColors.GetColors contains ManaColor.Red).
///   - Mana value 3 ({2}{R} = 3).
/// </summary>
public class BoggartBruteTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BoggartBrute_Identity_GoblinWarrior_3_2_AtCost2R()
    {
        var card = BoggartBruteFactory.Create(_alice);

        card.Name.Should().Be("Boggart Brute");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana value
    // -----------------------------------------------------------------------

    [Fact]
    public void BoggartBrute_ManaValue_IsThree()
    {
        var card = BoggartBruteFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(3, because: "{2}{R} = mana value 3");
    }

    // -----------------------------------------------------------------------
    // Color
    // -----------------------------------------------------------------------

    [Fact]
    public void BoggartBrute_IsRed()
    {
        var card = BoggartBruteFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.Red,
            because: "Boggart Brute has {R} in its mana cost");
    }

    // -----------------------------------------------------------------------
    // Menace
    // -----------------------------------------------------------------------

    [Fact]
    public void BoggartBrute_HasMenace()
    {
        var card = BoggartBruteFactory.Create(_alice);

        CombatAbilities.HasMenace(card).Should().BeTrue(
            because: "Boggart Brute has printed Menace (CR 702.110)");
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Menace");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BoggartBrute_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Boggart Brute", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Boggart Brute");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }
}
