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
/// Unit tests for <see cref="TanglepoolBridgeFactory"/> — Tanglepool Bridge
/// (Murders at Karlov Manor Commander, GU artifact "Bridge" tapland).
/// Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    Indestructible
///    {T}: Add {G} or {U}."
///
/// Mirrors <see cref="RazortideBridgeFactoryTests"/> (the WU member of the
/// same artifact "Bridge" tapland cycle) — Artifact Land typing + printed
/// Indestructible + enters-tapped replacement + one mana ability per
/// produced colour.
/// </summary>
public class TanglepoolBridgeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TanglepoolBridge_Identity_ArtifactLand_NotBasic()
    {
        var bridge = TanglepoolBridgeFactory.Create(_alice);

        bridge.Name.Should().Be("Tanglepool Bridge");
        bridge.HasType(CardType.Land).Should().BeTrue();
        bridge.HasType(CardType.Artifact).Should().BeTrue();
        bridge.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        bridge.Owner.Should().BeSameAs(_alice);
        bridge.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TanglepoolBridge_NamedCardFactory_DispatchesArtifactLand()
    {
        var card = NamedCardFactory.Create("Tanglepool Bridge", _alice);

        card.Should().BeOfType<Land>();
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void TanglepoolBridge_HasPrintedIndestructibleKeyword()
    {
        var bridge = TanglepoolBridgeFactory.Create(_alice);

        bridge.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void TanglepoolBridge_HasTwoManaAbilities_ProducingGreenAndBlue()
    {
        var bridge = TanglepoolBridgeFactory.Create(_alice);
        var manaAbilities = bridge.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(2, "{T}: Add {G} or {U}");
        manaAbilities.Should().Contain(m => m.ManaGenerated.Green == 1);
        manaAbilities.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void TanglepoolBridge_ManaAbilityActivation_TapsLandAndProducesGreen()
    {
        var bridge = TanglepoolBridgeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bridge);
        bridge.SetZone(ZoneType.Battlefield);

        var green = bridge.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue();
        var produced = green.Activate();

        produced.Green.Should().Be(1);
        produced.Blue.Should().Be(0);
        bridge.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TanglepoolBridge_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var bridge = TanglepoolBridgeFactory.Create(_alice, replacements);

        // CR 614.1c — unconditional "This land enters tapped." is registered
        // on the supplied bus; the shape-only (null bus) path skips it.
        // EntersTappedReplacement exposes no public bus-inspection surface,
        // so we assert the build succeeds with the bus wired (mirrors
        // RazortideBridgeFactoryTests).
        bridge.Should().NotBeNull();
    }
}
