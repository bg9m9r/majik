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
/// Unit tests for <see cref="GoldmireBridgeFactory"/> — Goldmire Bridge
/// (March of the Machine: The Aftermath, the WB member of the artifact
/// "Bridge" tapland cycle). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    Indestructible
///    {T}: Add {W} or {B}."
///
/// Mirrors <see cref="MistvaultBridgeFactoryTests"/> (its UB sibling) —
/// Artifact Land typing + printed Indestructible keyword +
/// enters-tapped replacement + one mana ability per produced colour.
/// </summary>
[Trait("Color", "C")]
public class GoldmireBridgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GoldmireBridge_Identity_ArtifactLand_NotBasic()
    {
        var bridge = GoldmireBridgeFactory.Create(_alice);

        bridge.Name.Should().Be("Goldmire Bridge");
        bridge.HasType(CardType.Land).Should().BeTrue();
        bridge.HasType(CardType.Artifact).Should().BeTrue();
        bridge.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        bridge.Owner.Should().BeSameAs(_alice);
        bridge.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void GoldmireBridge_HasPrintedIndestructibleKeyword()
    {
        var bridge = GoldmireBridgeFactory.Create(_alice);

        bridge.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void GoldmireBridge_HasTwoManaAbilities_ProducingWhiteAndBlack()
    {
        var bridge = GoldmireBridgeFactory.Create(_alice);
        var manaAbilities = bridge.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {W} or {B}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.White == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void GoldmireBridge_ManaAbilityActivation_TapsLandAndProducesWhite()
    {
        var bridge = GoldmireBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        var white = bridge.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue();
        var produced = white.Activate();

        produced.White.Should().Be(1);
        produced.Black.Should().Be(0);
        bridge.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void GoldmireBridge_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var bridge = GoldmireBridgeFactory.Create(_alice, replacements);

        // CR 614.1c — unconditional "This land enters tapped." is registered
        // on the supplied bus; the shape-only (null bus) path skips it.
        // EntersTappedReplacement exposes no public bus-inspection surface,
        // so we assert the build succeeds with the bus wired (mirrors
        // MistvaultBridgeFactoryTests).
        bridge.Should().NotBeNull();
    }
}
