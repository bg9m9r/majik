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
/// Unit tests for <see cref="ConcealedCourtyardFactory"/> — Kaladesh W/B fastland.
///
/// Covers card identity, the two mana abilities ({W} + {B}), and that no
/// triggered or non-mana activated abilities ship in v1 (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production).
/// </summary>
public class ConcealedCourtyardTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ConcealedCourtyard_IsLand()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ConcealedCourtyard_NameIsCorrect()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.Name.Should().Be("Concealed Courtyard");
    }

    [Fact]
    public void ConcealedCourtyard_OwnerAndControllerAreSet()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ConcealedCourtyard_IsNotLegendary()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ConcealedCourtyard_HasTwoManaAbilities()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void ConcealedCourtyard_HasWhiteManaAbility()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void ConcealedCourtyard_HasBlackManaAbility()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void ConcealedCourtyard_HasNoTriggeredAbilities()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-N-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void ConcealedCourtyard_HasNoActivatedAbilities()
    {
        var land = ConcealedCourtyardFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void ConcealedCourtyard_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Concealed Courtyard", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Concealed Courtyard");
    }
}
