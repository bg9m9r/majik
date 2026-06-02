using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CascadingCataractsFactory"/> — Kaladesh land.
///
/// Oracle text:
///   "Indestructible
///    {T}: Add {C}.
///    {5}, {T}: Add five mana in any combination of colors."
///
/// Covers:
/// - Land identity (non-Basic, no subtype) + <see cref="NamedCardFactory"/>
///   dispatch.
/// - Printed Indestructible keyword (CR 702.12) — mirrors Darksteel Citadel.
/// - {T}: Add {C} vanilla mana ability taps the land and produces colourless.
/// - {5}, {T}: Add five mana — the five-any-color modes are gated on {5} in
///   the pool and, when activated, pay {5} and add five mana of the chosen
///   combination (mirrors the FilterLand {N}-cost mana-ability shape and
///   Chromatic Star's any-color fan-out).
/// </summary>
[Trait("Color", "C")]
public class CascadingCataractsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CascadingCataracts_Identity_NonbasicLand()
    {
        var land = CascadingCataractsFactory.Create(_alice);

        land.Name.Should().Be("Cascading Cataracts");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Indestructible (CR 702.12)
    // -----------------------------------------------------------------------

    [Fact]
    public void CascadingCataracts_HasPrintedIndestructibleKeyword()
    {
        var land = CascadingCataractsFactory.Create(_alice);

        land.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void CascadingCataracts_HasColorlessManaAbility_ActivationTapsLandAndProducesC()
    {
        var land = CascadingCataractsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var colorless = land.Abilities.OfType<ManaAbility>().Single(IsColorlessOnly);

        colorless.CanActivate().Should().BeTrue();
        var produced = colorless.Activate();

        // {C} parses into the Generic slot (mirrors Darksteel Citadel /
        // Wasteland tap-for-{C} tests).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {5}, {T}: Add five mana in any combination of colors
    // -----------------------------------------------------------------------

    [Fact]
    public void CascadingCataracts_HasSixManaAbilities_OneColorlessPlusFiveAnyColorModes()
    {
        var land = CascadingCataractsFactory.Create(_alice);

        // 1 colourless ({T}: Add {C}) + 6 five-mana modes (WWWWW, UUUUU,
        // BBBBB, RRRRR, GGGGG, and the WUBRG five-color split).
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(7);
    }

    [Fact]
    public void CascadingCataracts_FiveManaMode_CannotActivateWithoutFiveGeneric()
    {
        var land = CascadingCataractsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var mode = FindFiveManaMode(land, "WWWWW")!;
        mode.Should().NotBeNull();
        mode.CanActivate().Should().BeFalse("the {5} activation cost is unpaid");
    }

    [Fact]
    public void CascadingCataracts_FiveManaMode_CanActivateWithFiveGeneric()
    {
        var land = CascadingCataractsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("5"));

        FindFiveManaMode(land, "WWWWW")!.CanActivate().Should().BeTrue();
        FindFiveManaMode(land, "WUBRG")!.CanActivate().Should().BeTrue();
    }

    [Fact]
    public void CascadingCataracts_FiveColorMode_Activation_PaysFive_AddsWUBRG()
    {
        var land = CascadingCataractsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // Seed {5} (the activation cost). Net result is +5 coloured pips and
        // the seeded generic spent.
        _alice.AddManaToPool(ManaCost.Parse("5"));
        var mode = FindFiveManaMode(land, "WUBRG")!;
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mode, _alice);

        _alice.ManaPool.White.Should().Be(1);
        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Black.Should().Be(1);
        _alice.ManaPool.Red.Should().Be(1);
        _alice.ManaPool.Green.Should().Be(1);
        _alice.ManaPool.Generic.Should().Be(0, "the seeded {5} was spent on the activation cost");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void CascadingCataracts_FiveColorMode_Activation_MonoWhite_AddsFiveWhite()
    {
        var land = CascadingCataractsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("5"));
        var mode = FindFiveManaMode(land, "WWWWW")!;
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mode, _alice);

        _alice.ManaPool.White.Should().Be(5);
        _alice.ManaPool.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void CascadingCataracts_FiveManaMode_CannotActivateWhenTapped()
    {
        var land = CascadingCataractsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("5"));
        // Pre-tap via the {C} mode (no {5} cost — leaves the seeded {5}).
        var activator = new ManaAbilityActivator();
        activator.ActivateManaAbility(
            land.Abilities.OfType<ManaAbility>().Single(IsColorlessOnly), _alice);
        land.IsTapped.Should().BeTrue();

        FindFiveManaMode(land, "WWWWW")!.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    [Fact]
    public void CascadingCataracts_HasNoActivatedOrTriggeredAbilities()
    {
        var land = CascadingCataractsFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void CascadingCataracts_Create_ThrowsOnNullOwner()
    {
        var act = () => CascadingCataractsFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ManaAbility? FindFiveManaMode(Land land, string pips)
    {
        var match = ManaCost.Parse(pips);
        return land.Abilities.OfType<ManaAbility>().SingleOrDefault(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green &&
            m.ManaGenerated.Generic == 0);
    }

    private static bool IsColorlessOnly(ManaAbility m) =>
        m.ManaGenerated.White == 0 &&
        m.ManaGenerated.Blue == 0 &&
        m.ManaGenerated.Black == 0 &&
        m.ManaGenerated.Red == 0 &&
        m.ManaGenerated.Green == 0 &&
        m.ManaGenerated.Generic == 1;
}
