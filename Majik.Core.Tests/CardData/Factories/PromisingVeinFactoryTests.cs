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
/// Tests for <see cref="PromisingVeinFactory"/> (The Lost Caverns of Ixalan,
/// Land — Cave).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice this land: Search your library for a basic land
///    card, put it onto the battlefield tapped, then shuffle."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: Land — Cave subtype (the printed type line).
/// - {T}: Add {C} (from JSON) — produces one colorless/generic, no extra cost.
/// - {1}, {T}, Sacrifice this land: fetch a basic land to the battlefield
///   tapped, leave nonbasics alone, self-sacrifice, then shuffle. Differs from
///   Terramorphic Expanse only by the added {1} mana component.
/// </summary>
[Trait("Color", "C")]
public class PromisingVeinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (non-vanilla subtype: Cave)
    // -----------------------------------------------------------------------

    [Fact]
    public void PromisingVein_Identity_IsCaveLand()
    {
        var land = PromisingVeinFactory.Create(_alice);

        land.Name.Should().Be("Promising Vein");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Cave).Should().BeTrue("printed type line is 'Land — Cave'");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} (from JSON)
    // -----------------------------------------------------------------------

    [Fact]
    public void PromisingVein_TapForColorless_ProducesC()
    {
        var land = PromisingVeinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var colorless = land.Abilities.OfType<ManaAbility>().Single();

        colorless.CanActivate().Should().BeTrue("the land is untapped and {C} needs no other cost");
        var mana = colorless.Activate();

        mana.Generic.Should().Be(1, "{T}: Add {C}");
        mana.White.Should().Be(0);
        mana.Blue.Should().Be(0);
        mana.Black.Should().Be(0);
        mana.Red.Should().Be(0);
        mana.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("{T} is the activation cost of the {C} ability");
    }

    // -----------------------------------------------------------------------
    // {1}, {T}, Sacrifice this land: search for a basic land, ETB tapped.
    // -----------------------------------------------------------------------

    [Fact]
    public void PromisingVein_SacAbility_DeclaresManaTapAndSacrificeCosts()
    {
        var land = PromisingVeinFactory.Create(_alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should()
            .ContainSingle("the printed {1} is part of the activation cost");
        ability.Costs.OfType<AdditionalCost>().Should()
            .Contain(ac => ac.CostType == AdditionalCostType.Tap);
        ability.Costs.OfType<AdditionalCost>().Should()
            .Contain(ac => ac.CostType == AdditionalCostType.Sacrifice);
    }

    [Fact]
    public void PromisingVein_Activation_FetchesBasicLandTapped_AndSacrifices()
    {
        // Stage a basic + a nonbasic dual-typed land in library; activation
        // must pick the basic and leave the dual alone (CR 205.4a).
        var basicForest = new Land(
            "Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        _alice.Zones.Library.AddCard(basicForest);
        _alice.Zones.Library.AddCard(stomping);
        basicForest.SetZone(ZoneType.Library);
        stomping.SetZone(ZoneType.Library);

        var vein = PromisingVeinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vein);
        vein.SetZone(ZoneType.Battlefield);

        var ability = vein.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        // Basic forest fetched to battlefield tapped; dual stays in library.
        _alice.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        basicForest.IsTapped.Should().BeTrue("the printed rider puts it onto the battlefield tapped");
        _alice.Zones.Library.GetCards().Should().Contain(stomping);
        _alice.Zones.Library.GetCards().Should().NotContain(basicForest);

        // Vein self-sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(vein);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(vein);
    }

    [Fact]
    public void PromisingVein_Activation_NoBasicInLibrary_StillSacrifices()
    {
        // Library contains only a nonbasic land — search finds nothing, but
        // the sacrifice still resolves.
        var stomping = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        _alice.Zones.Library.AddCard(stomping);
        stomping.SetZone(ZoneType.Library);

        var vein = PromisingVeinFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vein);
        vein.SetZone(ZoneType.Battlefield);

        var ability = vein.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        _alice.Zones.Graveyard.GetCards().Should().Contain(vein);
        // Nonbasic untouched.
        _alice.Zones.Library.GetCards().Should().Contain(stomping);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(stomping);
    }
}
