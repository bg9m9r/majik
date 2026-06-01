using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SpirebluffCanalFactory"/> — Kaladesh U/R fastland.
///
/// Covers card identity, the two mana abilities ({U} + {R}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production).
/// </summary>
public class SpirebluffCanalTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SpirebluffCanal_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SpirebluffCanal_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.Name.Should().Be("Spirebluff Canal");
    }

    [Fact]
    public void SpirebluffCanal_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpirebluffCanal_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SpirebluffCanal_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void SpirebluffCanal_HasBlueManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void SpirebluffCanal_HasRedManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void SpirebluffCanal_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void SpirebluffCanal_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Spirebluff Canal", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void SpirebluffCanal_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Spirebluff Canal", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Spirebluff Canal");
    }
}
