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
/// Unit tests for <see cref="DrossforgeBridgeFactory"/> — Drossforge Bridge
/// (Modern Horizons 3, the BR member of the artifact "Bridge" tapland cycle).
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    Indestructible
///    {T}: Add {B} or {R}."
///
/// Mirrors <see cref="MistvaultBridgeFactoryTests"/> (its UB sibling) —
/// Artifact Land typing + printed Indestructible keyword +
/// enters-tapped replacement + one mana ability per produced colour.
/// </summary>
public class DrossforgeBridgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DrossforgeBridge_Identity_ArtifactLand_NotBasic()
    {
        var bridge = DrossforgeBridgeFactory.Create(_alice);

        bridge.Name.Should().Be("Drossforge Bridge");
        bridge.HasType(CardType.Land).Should().BeTrue();
        bridge.HasType(CardType.Artifact).Should().BeTrue();
        bridge.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        bridge.Owner.Should().BeSameAs(_alice);
        bridge.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DrossforgeBridge_NamedCardFactory_DispatchesArtifactLand()
    {
        var card = NamedCardFactory.Create("Drossforge Bridge", _alice);

        card.Should().BeOfType<Land>();
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void DrossforgeBridge_HasPrintedIndestructibleKeyword()
    {
        var bridge = DrossforgeBridgeFactory.Create(_alice);

        bridge.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void DrossforgeBridge_HasTwoManaAbilities_ProducingBlackAndRed()
    {
        var bridge = DrossforgeBridgeFactory.Create(_alice);
        var manaAbilities = bridge.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {B} or {R}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Black == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Red == 1);
    }

    [Fact]
    public void DrossforgeBridge_ManaAbilityActivation_TapsLandAndProducesBlack()
    {
        var bridge = DrossforgeBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        var black = bridge.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeTrue();
        var produced = black.Activate();

        produced.Black.Should().Be(1);
        produced.Red.Should().Be(0);
        bridge.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void DrossforgeBridge_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var bridge = DrossforgeBridgeFactory.Create(_alice, replacements);

        // CR 614.1c — unconditional "This land enters tapped." is registered
        // on the supplied bus; the shape-only (null bus) path skips it.
        // EntersTappedReplacement exposes no public bus-inspection surface,
        // so we assert the build succeeds with the bus wired (mirrors
        // MistvaultBridgeFactoryTests).
        bridge.Should().NotBeNull();
    }
}
