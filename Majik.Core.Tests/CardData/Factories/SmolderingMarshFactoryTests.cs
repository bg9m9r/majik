using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SmolderingMarshFactory"/> — the Battle for
/// Zendikar B/R "battle land" (a.k.a. "tango land"). Oracle text
/// (Scryfall, verified):
///   "({T}: Add {B} or {R}.)
///    This land enters tapped unless you control two or more basic lands."
/// Type line: "Land — Swamp Mountain".
///
/// Covers:
/// - Identity (Land + the two printed land subtypes Swamp / Mountain,
///   nonbasic, non-Legendary).
/// - Two mana abilities producing {B} and {R} (CR 605.1 — mana abilities
///   don't use the stack).
/// - No triggered / non-mana activated abilities ship here: the conditional
///   "enters tapped unless you control two or more basic lands" (CR 614.1c)
///   is a replacement effect handled by the binder layer on the production
///   load path, same posture as <see cref="BloomingMarshFactory"/> and
///   <see cref="ZagothTriomeFactory"/>.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
public class SmolderingMarshFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SmolderingMarsh_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SmolderingMarsh_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.Name.Should().Be("Smoldering Marsh");
    }

    [Fact]
    public void SmolderingMarsh_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SmolderingMarsh_HasSwampAndMountainSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.HasSubtype(CardSubtype.Swamp).Should().BeTrue();
        land.HasSubtype(CardSubtype.Mountain).Should().BeTrue();
    }

    [Fact]
    public void SmolderingMarsh_IsNotBasic_NotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("battle lands are nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SmolderingMarsh_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void SmolderingMarsh_HasBlackManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void SmolderingMarsh_HasRedManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void SmolderingMarsh_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-two-or-more-basic-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void SmolderingMarsh_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Smoldering Marsh", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void SmolderingMarsh_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Smoldering Marsh", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Smoldering Marsh");
    }
}
