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
/// Tests for <see cref="CabarettiCourtyardFactory"/> (Streets of New Capenna —
/// the Cabaretti / Naya member of the "slow fetch" land cycle).
///
/// Oracle (verified against Scryfall 2026-06-14):
///   <c>When this land enters, sacrifice it. When you do, search your library
///   for a basic Mountain, Forest, or Plains card, put it onto the battlefield
///   tapped, then shuffle and you gain 1 life.</c>
///
/// Same fetch-onto-battlefield-tapped + shuffle idiom as
/// <see cref="FabledPassageFactory"/> / <see cref="EsperPanoramaFactory"/>, but
/// the fetch sits on an ETB-sacrifice TRIGGER (not an activated ability),
/// narrows to basic Mountain / Forest / Plains (CR 205.3), and carries the
/// "you gain 1 life" rider (CR 119.3).
/// </summary>
[Trait("Color", "C")]
public class CabarettiCourtyardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land MakeBasic(string name, CardSubtype subtype) =>
        new(name, new[] { CardSupertype.Basic }, new[] { subtype });

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_ProducesNonbasicLand_NoSupertypeNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Cabaretti Courtyard", _alice);

        land.Name.Should().Be("Cabaretti Courtyard");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("the Courtyard is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ETB-sacrifice fetch trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HasSingleEtbTriggeredFetchAbility()
    {
        var land = (Land)NamedCardFactory.Create("Cabaretti Courtyard", _alice);

        // The fetch sits on an ETB triggered ability — NOT an activated
        // ability (unlike the Panorama cycle).
        land.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<ManaAbility>().Should().BeEmpty("the Courtyard makes no mana on its own");
    }

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_FetchesBasicMountainTapped_SacrificesSelf_AndGainsLife()
    {
        var basicMountain = MakeBasic("Mountain", CardSubtype.Mountain);
        // A basic Island is NOT a legal target (only Mountain/Forest/Plains).
        var basicIsland = MakeBasic("Island", CardSubtype.Island);
        _alice.Zones.Library.AddCard(basicMountain);
        _alice.Zones.Library.AddCard(basicIsland);
        basicMountain.SetZone(ZoneType.Library);
        basicIsland.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Cabaretti Courtyard", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in trigger.Effects) eff.Execute();

        // Basic Mountain fetched to battlefield tapped; off-type Island untouched.
        _alice.Zones.Battlefield.GetCards().Should().Contain(basicMountain);
        basicMountain.IsTapped.Should().BeTrue("put onto the battlefield tapped");
        _alice.Zones.Library.GetCards().Should().Contain(basicIsland,
            "Island is not a Mountain/Forest/Plains");
        _alice.Zones.Library.GetCards().Should().NotContain(basicMountain);

        // The Courtyard sacrificed itself.
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);

        // "and you gain 1 life" (CR 119.3).
        _alice.LifeTotal.Should().Be(21);
    }

    [Theory]
    [InlineData("Forest", CardSubtype.Forest)]
    [InlineData("Plains", CardSubtype.Plains)]
    public void Resolve_FetchesBasicForestOrPlains_AreLegalTargets(string name, CardSubtype subtype)
    {
        var basic = MakeBasic(name, subtype);
        _alice.Zones.Library.AddCard(basic);
        basic.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Cabaretti Courtyard", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in trigger.Effects) eff.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(basic);
        basic.IsTapped.Should().BeTrue();
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void Resolve_NoLegalBasic_StillSacrificesAndGainsLife()
    {
        // Only a nonbasic dual in library — search finds nothing, but the
        // sacrifice + the unconditional life gain still happen.
        var dual = new Land(
            "Stomping Ground", supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Cabaretti Courtyard", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in trigger.Effects) eff.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        // Nonbasic untouched (only basics are legal AND only M/F/P subtypes).
        _alice.Zones.Library.GetCards().Should().Contain(dual);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);
        // "you gain 1 life" resolves regardless of the search outcome.
        _alice.LifeTotal.Should().Be(21);
    }
}
