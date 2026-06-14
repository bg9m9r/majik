using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ObscuraStorefrontFactory"/> (Streets of New Capenna —
/// the Obscura member of the "storefront" land cycle).
///
/// Oracle (verified against Scryfall 2026-06-14):
///   <c>When this land enters, sacrifice it. When you do, search your library
///      for a basic Plains, Island, or Swamp card, put it onto the battlefield
///      tapped, then shuffle and you gain 1 life.</c>
///
/// The card is a single ETB triggered ability (CR 603.6a) that collapses the
/// "sacrifice it" + reflexive "When you do, search …" (CR 603.6e) into one
/// mandatory resolve: sacrifice self (CR 701.16), tutor a basic Plains / Island
/// / Swamp onto the battlefield tapped (CR 205.4a / CR 614), shuffle
/// (CR 701.20a), then gain 1 life (CR 119.3). A colorless land.
/// </summary>
[Trait("Color", "C")]
public class ObscuraStorefrontFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_ProducesNonbasicLand_NoSupertypeNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Obscura Storefront", _alice);

        land.Name.Should().Be("Obscura Storefront");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should()
            .BeFalse("Obscura Storefront is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ETB trigger — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HasSingleEtbTrigger_BattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Obscura Storefront", _alice);

        var triggers = land.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1,
            "Obscura Storefront prints one triggered ability — the ETB sac-and-fetch.");
        triggers[0].Source.Should().BeSameAs(land);
        triggers[0].Controller.Should().BeSameAs(_alice);
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void HasNoManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Obscura Storefront", _alice);

        // CR 305.6 — Obscura Storefront produces no mana on its own; it only
        // fetches a basic.
        land.Abilities.OfType<ManaAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ETB resolution — sacrifice + fetch basic Island tapped + shuffle + life
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbResolution_SacrificesSelf_FetchesBasicIslandTapped_GainsLife()
    {
        var basicIsland = new Land(
            "Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        // A basic Forest is NOT a legal target (only Plains/Island/Swamp).
        var basicForest = new Land(
            "Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        _alice.Zones.Library.AddCard(basicIsland);
        _alice.Zones.Library.AddCard(basicForest);
        basicIsland.SetZone(ZoneType.Library);
        basicForest.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Obscura Storefront", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in trigger.Effects) eff.Execute();

        // Basic Island fetched to battlefield tapped; off-color Forest untouched.
        _alice.Zones.Battlefield.GetCards().Should().Contain(basicIsland);
        basicIsland.IsTapped.Should().BeTrue("put onto the battlefield tapped");
        _alice.Zones.Library.GetCards().Should().Contain(basicForest,
            "Forest is not a Plains/Island/Swamp");
        _alice.Zones.Library.GetCards().Should().NotContain(basicIsland);

        // Obscura Storefront self-sacrificed (CR 701.16).
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);

        // "and you gain 1 life" (CR 119.3).
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void EtbResolution_FetchesBasicPlains_IsLegalTarget()
    {
        var basicPlains = new Land(
            "Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        _alice.Zones.Library.AddCard(basicPlains);
        basicPlains.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Obscura Storefront", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in trigger.Effects) eff.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(basicPlains);
        basicPlains.IsTapped.Should().BeTrue();
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void EtbResolution_FetchesBasicSwamp_IsLegalTarget()
    {
        var basicSwamp = new Land(
            "Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        _alice.Zones.Library.AddCard(basicSwamp);
        basicSwamp.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Obscura Storefront", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in trigger.Effects) eff.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(basicSwamp);
        basicSwamp.IsTapped.Should().BeTrue();
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void EtbResolution_NoLegalBasic_StillSacrificesAndGainsLife()
    {
        // Only a nonbasic dual in library — search finds nothing, but the
        // sacrifice + lifegain still happen (CR 701.20a — shuffle/lifegain
        // resolve regardless of whether a card was found).
        var dual = new Land(
            "Watery Grave", supertypes: null,
            new[] { CardSubtype.Island, CardSubtype.Swamp });
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Obscura Storefront", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in trigger.Effects) eff.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        // Nonbasic untouched (only basics with a P/I/S subtype are legal).
        _alice.Zones.Library.GetCards().Should().Contain(dual);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);

        // Lifegain still happens.
        _alice.LifeTotal.Should().Be(21);
    }
}
