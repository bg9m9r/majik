using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="EsperPanoramaFactory"/> (Alara Reborn — the Esper
/// member of the paid-fetch "Panorama" cycle).
///
/// Oracle (verified against Scryfall 2026-06-02):
///   <c>{T}: Add {C}.</c>
///   <c>{1}, {T}, Sacrifice this land: Search your library for a basic Plains,
///      Island, or Swamp card, put it onto the battlefield tapped, then
///      shuffle.</c>
///
/// Composes two already-supported idioms:
/// - <c>{T}: Add {C}</c> vanilla <see cref="ManaAbility"/> (CR 107.4c — {C} is
///   colorless mana, modeled as +1 generic), materialised from JSON.
/// - The Bant Panorama tutor-onto-battlefield-tapped idiom narrowed to
///   basic Plains / Island / Swamp (CR 205.4a), with the extra generic {1}
///   on the fetch cost (CR 117.5).
/// </summary>
[Trait("Color", "C")]
public class EsperPanoramaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_ProducesNonbasicLand_NoSupertypeNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Esper Panorama", _alice);

        land.Name.Should().Be("Esper Panorama");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Panoramas are nonbasic lands");
        land.Subtypes.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void HasManaAbilityProducingColorless()
    {
        var land = (Land)NamedCardFactory.Create("Esper Panorama", _alice);

        var mana = land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle().Subject;

        // CR 107.4c — {C} is colorless mana, modeled as +1 generic.
        mana.ManaGenerated.Generic.Should().Be(1, "{T}: Add {C}");
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Black.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Fetch ability shape — {1}, {T}, Sacrifice this land.
    // -----------------------------------------------------------------------

    [Fact]
    public void HasTapSacrificeFetchActivatedAbility_WithGenericOne()
    {
        var land = (Land)NamedCardFactory.Create("Esper Panorama", _alice);

        // The fetch ability is the ActivatedAbility carrying the Tap cost.
        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));

        fetch.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);

        // CR 117.5 — the printed cost carries an extra generic {1}.
        var manaCost = fetch.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(1, "fetch costs {1} in addition to {T}, Sacrifice");
    }

    [Fact]
    public void Activation_FetchesBasicIslandTapped_AndSacrifices()
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

        var land = (Land)NamedCardFactory.Create("Esper Panorama", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));
        foreach (var eff in fetch.Effects) eff.Execute();

        // Basic Island fetched to battlefield tapped; off-color Forest untouched.
        _alice.Zones.Battlefield.GetCards().Should().Contain(basicIsland);
        basicIsland.IsTapped.Should().BeTrue("put onto the battlefield tapped");
        _alice.Zones.Library.GetCards().Should().Contain(basicForest,
            "Forest is not a Plains/Island/Swamp");
        _alice.Zones.Library.GetCards().Should().NotContain(basicIsland);

        // Esper Panorama self-sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void Activation_FetchesBasicPlains_AndBasicSwamp_AreLegalTargets()
    {
        var basicPlains = new Land(
            "Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        _alice.Zones.Library.AddCard(basicPlains);
        basicPlains.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Esper Panorama", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));
        foreach (var eff in fetch.Effects) eff.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(basicPlains);
        basicPlains.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Activation_FetchesBasicSwamp_IsLegalTarget()
    {
        var basicSwamp = new Land(
            "Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        _alice.Zones.Library.AddCard(basicSwamp);
        basicSwamp.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Esper Panorama", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));
        foreach (var eff in fetch.Effects) eff.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(basicSwamp);
        basicSwamp.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Activation_NoLegalBasic_StillSacrifices()
    {
        // Only a nonbasic dual in library — search finds nothing, but the
        // sacrifice still happens.
        var dual = new Land(
            "Watery Grave", supertypes: null,
            new[] { CardSubtype.Island, CardSubtype.Swamp });
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Esper Panorama", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var fetch = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Tap));
        foreach (var eff in fetch.Effects) eff.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        // Nonbasic untouched (only basics are legal AND only P/I/S subtypes).
        _alice.Zones.Library.GetCards().Should().Contain(dual);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);
    }
}
