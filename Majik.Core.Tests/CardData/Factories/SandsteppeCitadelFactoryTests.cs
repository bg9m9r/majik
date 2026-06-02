using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SandsteppeCitadelFactory"/> — Sandsteppe
/// Citadel (Khans of Tarkir "tapped tri-land" cycle). Oracle text:
///   "This land enters tapped.
///    {T}: Add {W}, {B}, or {G}."
///
/// Unlike the Ikoria Triomes, Sandsteppe Citadel carries no printed land
/// subtypes and no Cycling. It is a plain colourless Land that taps for
/// one of three colours and enters tapped. Covers:
/// - Identity (Land, no subtypes).
/// - Three mana abilities producing {W}, {B}, {G} respectively (CR 605.1).
/// - No Cycling / activated ability (unlike the Triomes).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class SandsteppeCitadelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SandsteppeCitadel_IsLand_WithNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Sandsteppe Citadel", _alice);

        land.Should().NotBeNull();
        land.Name.Should().Be("Sandsteppe Citadel");
        land.Subtypes.Should().BeEmpty("Sandsteppe Citadel has no printed land subtypes");
    }

    [Fact]
    public void SandsteppeCitadel_OwnerAndControllerAreSet()
    {
        var land = SandsteppeCitadelFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SandsteppeCitadel_HasThreeManaAbilities_ProducingWBG()
    {
        var land = (Land)NamedCardFactory.Create("Sandsteppe Citadel", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "{T}: Add {W}, {B}, or {G}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void SandsteppeCitadel_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Sandsteppe Citadel", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Sandsteppe Citadel has no Cycling (unlike the Triomes)");
    }
}
