using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CentaurCourserFactory"/> (Magic 2010, {2}{G}).
///
/// Creature — Centaur Warrior 3/3. Oracle text (verified against Scryfall):
/// empty — Centaur Courser is a plain vanilla creature (no printed keywords,
/// triggers, statics, or activated abilities).
///
/// Covers:
///   - Identity (name, cost, P/T, Creature, Centaur + Warrior subtypes,
///     owner / controller, green color).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Vanilla: no keyword / triggered / activated / mana abilities.
/// </summary>
public class CentaurCourserFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CentaurCourser_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Centaur Courser", _alice);

        c.Name.Should().Be("Centaur Courser");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Centaur).Should().BeTrue("CR 205.3m");
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue("CR 205.3m");
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CentaurCourser_IsGreen()
    {
        var c = (Creature)NamedCardFactory.Create("Centaur Courser", _alice);

        // {2}{G} = 2 generic + 1 green = CMC 3 (CR 202.3); the {G} makes it green.
        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Centaur Courser has {G} in its mana cost");
    }

    [Fact]
    public void CentaurCourser_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Centaur Courser", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Centaur Courser");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Centaur).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void CentaurCourser_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Centaur Courser", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Centaur Courser is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
    }
}
