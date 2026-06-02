using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="JungleShrineFactory"/> — Jungle Shrine
/// (Shards of Alara "tapped tri-land" cycle). Oracle text:
///   "This land enters tapped.
///    {T}: Add {R}, {G}, or {W}."
///
/// Unlike the Ikoria Triomes, Jungle Shrine carries no printed land
/// subtypes and no Cycling. It is a plain colourless Land that taps for
/// one of three colours and enters tapped. Covers:
/// - Identity (Land, no subtypes).
/// - Three mana abilities producing {R}, {G}, {W} respectively (CR 605.1).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class JungleShrineFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void JungleShrine_IsLand_WithNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Jungle Shrine", _alice);

        land.Should().NotBeNull();
        land.Name.Should().Be("Jungle Shrine");
        land.Subtypes.Should().BeEmpty("Jungle Shrine has no printed land subtypes");
    }

    [Fact]
    public void JungleShrine_HasThreeManaAbilities_ProducingRGW()
    {
        var land = (Land)NamedCardFactory.Create("Jungle Shrine", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(3, "{T}: Add {R}, {G}, or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void JungleShrine_HasNoCyclingAbility()
    {
        var land = (Land)NamedCardFactory.Create("Jungle Shrine", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Jungle Shrine has no Cycling (unlike the Triomes)");
    }
}
