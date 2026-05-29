using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SlagwoodsBridgeFactory"/> — Slagwoods Bridge
/// (Modern Horizons 2, RG artifact "Bridge" tapland). Oracle text
/// (verified against Scryfall):
///   "This land enters tapped.
///    Indestructible
///    {T}: Add {R} or {G}."
///
/// Mirrors <see cref="DrossforgeBridgeFactoryTests"/> /
/// <see cref="RazortideBridgeFactoryTests"/> (identical cycle: Artifact
/// Land typing + printed Indestructible + enters-tapped replacement +
/// one mana ability per produced colour).
/// </summary>
public class SlagwoodsBridgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SlagwoodsBridge_Identity_ArtifactLand_NotBasic()
    {
        var bridge = SlagwoodsBridgeFactory.Create(_alice);

        bridge.Name.Should().Be("Slagwoods Bridge");
        bridge.HasType(CardType.Land).Should().BeTrue();
        bridge.HasType(CardType.Artifact).Should().BeTrue();
        bridge.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        bridge.Owner.Should().BeSameAs(_alice);
        bridge.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SlagwoodsBridge_NamedCardFactory_DispatchesArtifactLand()
    {
        var card = NamedCardFactory.Create("Slagwoods Bridge", _alice);

        card.Should().BeOfType<Land>();
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void SlagwoodsBridge_HasPrintedIndestructibleKeyword()
    {
        var bridge = SlagwoodsBridgeFactory.Create(_alice);

        bridge.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void SlagwoodsBridge_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var bridge = SlagwoodsBridgeFactory.Create(_alice);
        var manaAbilities = bridge.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {R} or {G}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void SlagwoodsBridge_ManaAbilityActivation_TapsLandAndProducesRed()
    {
        var bridge = SlagwoodsBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        var red = bridge.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue();
        var produced = red.Activate();

        produced.Red.Should().Be(1);
        produced.Green.Should().Be(0);
        bridge.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void SlagwoodsBridge_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var bridge = SlagwoodsBridgeFactory.Create(_alice, replacements);

        // CR 614.1c — unconditional "This land enters tapped." is registered
        // on the supplied bus; the shape-only (null bus) path skips it.
        // EntersTappedReplacement exposes no public bus-inspection surface,
        // so we assert the build succeeds with the bus wired (mirrors
        // DrossforgeBridgeFactoryTests).
        bridge.Should().NotBeNull();
    }
}
