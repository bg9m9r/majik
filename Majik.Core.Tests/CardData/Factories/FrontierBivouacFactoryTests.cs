using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FrontierBivouacFactory"/> — Frontier Bivouac
/// (Khans of Tarkir "tapped tri-land" cycle). Oracle text:
///   "This land enters tapped.
///    {T}: Add {G}, {U}, or {R}."
///
/// Like Jungle Shrine (and unlike the Ikoria Triomes), Frontier Bivouac
/// carries no printed land subtypes and no Cycling. It is a plain
/// colourless Land that taps for one of three colours and enters tapped.
/// Covers:
/// - Identity (Land, no subtypes).
/// - Three mana abilities producing {G}, {U}, {R} respectively (CR 605.1).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class FrontierBivouacFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FrontierBivouac_IsLand_WithNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Frontier Bivouac", _alice);

        land.Should().NotBeNull();
        land.Name.Should().Be("Frontier Bivouac");
        land.Subtypes.Should().BeEmpty("Frontier Bivouac has no printed land subtypes");
    }

    [Fact]
    public void FrontierBivouac_HasThreeManaAbilities_ProducingGUR()
    {
        var land = (Land)NamedCardFactory.Create("Frontier Bivouac", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "{T}: Add {G}, {U}, or {R}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void FrontierBivouac_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Frontier Bivouac", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Frontier Bivouac has no Cycling (unlike the Triomes)");
    }
}
