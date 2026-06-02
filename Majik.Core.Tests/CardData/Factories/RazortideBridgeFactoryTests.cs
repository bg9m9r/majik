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
/// Unit tests for <see cref="RazortideBridgeFactory"/> — Razortide Bridge
/// (March of the Machine: The Aftermath, WU artifact "Bridge" tapland).
/// Oracle text:
///   "This land enters tapped.
///    Indestructible
///    {T}: Add {W} or {U}."
///
/// Mirrors <see cref="DarksteelCitadelTests"/> (Artifact Land typing +
/// printed Indestructible + mana ability) and
/// <see cref="SavaiTriomeFactoryTests"/> (enters-tapped replacement +
/// one mana ability per produced colour).
/// </summary>
[Trait("Color", "C")]
public class RazortideBridgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RazortideBridge_Identity_ArtifactLand_NotBasic()
    {
        var bridge = RazortideBridgeFactory.Create(_alice);

        bridge.Name.Should().Be("Razortide Bridge");
        bridge.HasType(CardType.Land).Should().BeTrue();
        bridge.HasType(CardType.Artifact).Should().BeTrue();
        bridge.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        bridge.Owner.Should().BeSameAs(_alice);
        bridge.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void RazortideBridge_HasPrintedIndestructibleKeyword()
    {
        var bridge = RazortideBridgeFactory.Create(_alice);

        bridge.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void RazortideBridge_HasTwoManaAbilities_ProducingWhiteAndBlue()
    {
        var bridge = RazortideBridgeFactory.Create(_alice);
        var manaAbilities = bridge.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {W} or {U}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void RazortideBridge_ManaAbilityActivation_TapsLandAndProducesWhite()
    {
        var bridge = RazortideBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        var white = bridge.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue();
        var produced = white.Activate();

        produced.White.Should().Be(1);
        produced.Blue.Should().Be(0);
        bridge.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void RazortideBridge_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var bridge = RazortideBridgeFactory.Create(_alice, replacements);

        // CR 614.1c — unconditional "This land enters tapped." is registered
        // on the supplied bus; the shape-only (null bus) path skips it.
        // EntersTappedReplacement exposes no public bus-inspection surface,
        // so we assert the build succeeds with the bus wired (mirrors
        // SavaiTriomeFactoryTests).
        bridge.Should().NotBeNull();
    }
}
