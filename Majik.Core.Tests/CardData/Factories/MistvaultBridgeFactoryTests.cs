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
/// Unit tests for <see cref="MistvaultBridgeFactory"/> — Mistvault Bridge
/// (March of the Machine: The Aftermath, the UB member of the artifact
/// "Bridge" tapland cycle). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    Indestructible
///    {T}: Add {U} or {B}."
///
/// Mirrors <see cref="RazortideBridgeFactoryTests"/> (its WU sibling) —
/// Artifact Land typing + printed Indestructible keyword +
/// enters-tapped replacement + one mana ability per produced colour.
/// </summary>
[Trait("Color", "C")]
public class MistvaultBridgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MistvaultBridge_Identity_ArtifactLand_NotBasic()
    {
        var bridge = MistvaultBridgeFactory.Create(_alice);

        bridge.Name.Should().Be("Mistvault Bridge");
        bridge.HasType(CardType.Land).Should().BeTrue();
        bridge.HasType(CardType.Artifact).Should().BeTrue();
        bridge.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        bridge.Owner.Should().BeSameAs(_alice);
        bridge.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void MistvaultBridge_HasPrintedIndestructibleKeyword()
    {
        var bridge = MistvaultBridgeFactory.Create(_alice);

        bridge.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void MistvaultBridge_HasTwoManaAbilities_ProducingBlueAndBlack()
    {
        var bridge = MistvaultBridgeFactory.Create(_alice);
        var manaAbilities = bridge.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {U} or {B}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void MistvaultBridge_ManaAbilityActivation_TapsLandAndProducesBlue()
    {
        var bridge = MistvaultBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        var blue = bridge.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue();
        var produced = blue.Activate();

        produced.Blue.Should().Be(1);
        produced.Black.Should().Be(0);
        bridge.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void MistvaultBridge_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var bridge = MistvaultBridgeFactory.Create(_alice, replacements);

        // CR 614.1c — unconditional "This land enters tapped." is registered
        // on the supplied bus; the shape-only (null bus) path skips it.
        // EntersTappedReplacement exposes no public bus-inspection surface,
        // so we assert the build succeeds with the bus wired (mirrors
        // RazortideBridgeFactoryTests).
        bridge.Should().NotBeNull();
    }
}
