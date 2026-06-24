using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <c>VibrantCityscapeFactory</c> (Bloomburrow — a functional reprint
/// of Evolving Wilds / Terramorphic Expanse).
///
/// Oracle (verified against Scryfall 2026-06-24):
///   <c>{T}, Sacrifice this land: Search your library for a basic land card, put
///   it onto the battlefield tapped, then shuffle.</c>
///
/// Same sac-to-fetch shape as Terramorphic Expanse — any basic land (no subtype
/// restriction, unlike the Panoramas), entering <b>tapped</b>, with no mana
/// component and no life payment in the activation cost.
/// </summary>
[Trait("Color", "C")]
public class VibrantCityscapeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity — nonbasic, colorless Land, no supertype/subtype, no mana of its own.
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_ProducesNonbasicLand_NoSupertypeNoSubtypes_NoManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Vibrant Cityscape", _alice);

        land.Name.Should().Be("Vibrant Cityscape");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("fetch lands are nonbasic");
        land.Subtypes.Should().BeEmpty();
        // CR 305.6 — no intrinsic mana ability (it produces no mana on its own).
        land.Abilities.OfType<ManaAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Fetch ability shape — {T}, Sacrifice this land (no mana cost).
    // -----------------------------------------------------------------------

    [Fact]
    public void HasTapSacrificeFetchActivatedAbility_NoManaCost()
    {
        var land = (Land)NamedCardFactory.Create("Vibrant Cityscape", _alice);

        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Should().ContainSingle().Subject;

        fetch.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
        // No printed {N} — only {T} + Sacrifice (unlike the {1} Panoramas).
        fetch.Costs.OfType<ManaCostCost>().Should().BeEmpty("the printed cost is only {T}, Sacrifice");
    }

    // -----------------------------------------------------------------------
    // Behaviour — fetch ANY basic (no subtype filter), tapped, then sacrifice.
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_FetchesAnyBasicTapped_AndSacrifices()
    {
        // A basic Mountain — off-color for a Panorama, but a fully legal target
        // here because Vibrant Cityscape fetches ANY basic land (CR 205.4a).
        var basicMountain = new Land(
            "Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        // A nonbasic dual must NOT be picked — only basics qualify.
        var dual = new Land(
            "Stomping Ground", supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        _alice.Zones.Library.AddCard(basicMountain);
        _alice.Zones.Library.AddCard(dual);
        basicMountain.SetZone(ZoneType.Library);
        dual.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Vibrant Cityscape", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>().Single();
        // CR 117.5 — tap + sacrifice are activation COSTS (paid now), then the
        // search effect resolves with this land already off the battlefield.
        foreach (var cost in fetch.Costs.OfType<AdditionalCost>()) cost.Pay(_alice);
        foreach (var eff in fetch.Effects) eff.Execute();

        // Basic Mountain fetched to battlefield tapped; nonbasic dual untouched.
        _alice.Zones.Battlefield.GetCards().Should().Contain(basicMountain);
        basicMountain.IsTapped.Should().BeTrue("put onto the battlefield tapped");
        _alice.Zones.Library.GetCards().Should().Contain(dual, "only basic lands qualify");
        _alice.Zones.Library.GetCards().Should().NotContain(basicMountain);

        // Vibrant Cityscape self-sacrificed; no life payment (unlike fetchlands).
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Activation_NoBasicInLibrary_StillSacrifices()
    {
        // Library has only a nonbasic land — search finds nothing, but the
        // sacrifice cost is still paid (CR 117.5 / 701.39c).
        var dual = new Land(
            "Stomping Ground", supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Vibrant Cityscape", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in fetch.Costs.OfType<AdditionalCost>()) cost.Pay(_alice);
        foreach (var eff in fetch.Effects) eff.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.LifeTotal.Should().Be(20);
        _alice.Zones.Library.GetCards().Should().Contain(dual);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);
    }
}
