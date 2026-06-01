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
/// Unit tests for <see cref="DreamrootCascadeFactory"/> — Wilds of Eldraine
/// G/U "slow land" cycle. Oracle text (Scryfall, verified):
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {G} or {U}."
/// Type line: "Land" (no land subtypes).
///
/// Covers card identity, the two mana abilities ({G} + {U}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped "two or more other lands" clause, CR 614.1c, is a replacement
/// effect handled by <see cref="ConditionalEntersTappedBinder"/> on the
/// production load path).
/// </summary>
public class DreamrootCascadeTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DreamrootCascade_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void DreamrootCascade_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.Name.Should().Be("Dreamroot Cascade");
    }

    [Fact]
    public void DreamrootCascade_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DreamrootCascade_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void DreamrootCascade_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void DreamrootCascade_HasGreenManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void DreamrootCascade_HasBlueManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void DreamrootCascade_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-two-or-more-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void DreamrootCascade_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Dreamroot Cascade", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void DreamrootCascade_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Dreamroot Cascade", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Dreamroot Cascade");
    }
}
