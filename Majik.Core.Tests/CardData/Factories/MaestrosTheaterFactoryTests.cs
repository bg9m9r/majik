using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MaestrosTheaterFactory"/> — Streets of New Capenna
/// tri-fetch Land. Oracle (verified against Scryfall 2026-06-14):
///   "When this land enters, sacrifice it. When you do, search your library for
///    a basic Island, Swamp, or Mountain card, put it onto the battlefield
///    tapped, then shuffle and you gain 1 life."
///
/// Covers ONLY the card's unique behaviour (the ETB-sac → tri-basic fetch
/// tapped → gain life mechanic); card-name / type / dispatch well-formedness is
/// asserted for every implemented card by
/// <c>Majik.Core.Tests.CardData.CardFactoryContractTests</c>.
///
/// The card produces no mana itself and is colourless (it is a Land with no
/// colour); sharded under "C" like the Esper Panorama tri-fetch sibling.
/// </summary>
[Trait("Color", "C")]
public class MaestrosTheaterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land Basic(string name, CardSubtype subtype, Player owner)
    {
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        land.SetOwner(owner);
        return land;
    }

    private TriggeredAbility CreateOnBattlefieldWithEtb()
    {
        var land = MaestrosTheaterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land.Abilities.OfType<TriggeredAbility>().Single();
    }

    // -----------------------------------------------------------------------
    // Ability shape: exactly one ETB trigger, no mana/activated abilities.
    // -----------------------------------------------------------------------

    [Fact]
    public void HasExactlyOneEtbTrigger_NoManaOrActivatedAbilities()
    {
        var land = MaestrosTheaterFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Maestros Theater produces no mana on its own (CR 305.6)");
        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // ETB: sacrifice self + fetch a basic Island/Swamp/Mountain tapped + gain 1.
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_SacrificesSelf_FetchesBasicIslandTapped_AndGainsOneLife()
    {
        var island = Basic("Island", CardSubtype.Island, _alice);
        // A basic Forest is NOT a legal target (only Island/Swamp/Mountain).
        var forest = Basic("Forest", CardSubtype.Forest, _alice);
        _alice.Zones.Library.AddCard(island);
        _alice.Zones.Library.AddCard(forest);
        island.SetZone(ZoneType.Library);
        forest.SetZone(ZoneType.Library);

        var etb = CreateOnBattlefieldWithEtb();
        var theater = (Land)etb.Source;
        var startLife = _alice.LifeTotal;

        etb.Resolve();

        // Basic Island fetched to battlefield tapped; off-colour Forest untouched.
        _alice.Zones.Battlefield.GetCards().Should().Contain(island);
        island.IsTapped.Should().BeTrue("put onto the battlefield tapped");
        _alice.Zones.Library.GetCards().Should().Contain(forest,
            "Forest is not a basic Island/Swamp/Mountain");
        _alice.Zones.Library.GetCards().Should().NotContain(island);

        // Maestros Theater self-sacrificed (CR 701.16).
        _alice.Zones.Graveyard.GetCards().Should().Contain(theater);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(theater);

        // CR 119.3 — "you gain 1 life".
        _alice.LifeTotal.Should().Be(startLife + 1);
    }

    [Theory]
    [InlineData("Swamp")]
    [InlineData("Mountain")]
    public void Etb_FetchesEachOfSwampAndMountain_Tapped(string subtypeName)
    {
        var subtype = subtypeName == "Swamp" ? CardSubtype.Swamp : CardSubtype.Mountain;
        var basic = Basic(subtypeName, subtype, _alice);
        _alice.Zones.Library.AddCard(basic);
        basic.SetZone(ZoneType.Library);

        var etb = CreateOnBattlefieldWithEtb();
        etb.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(basic);
        basic.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Etb_NoLegalBasic_StillSacrificesAndGainsLife()
    {
        // A nonbasic dual + an off-colour basic Plains — neither is a legal
        // fetch, but the sacrifice and the lifegain still happen.
        var dual = new Land("Watery Grave",
            supertypes: null,
            subtypes: new[] { CardSubtype.Island, CardSubtype.Swamp });
        dual.SetOwner(_alice);
        var plains = Basic("Plains", CardSubtype.Plains, _alice);
        _alice.Zones.Library.AddCard(dual);
        _alice.Zones.Library.AddCard(plains);
        dual.SetZone(ZoneType.Library);
        plains.SetZone(ZoneType.Library);

        var etb = CreateOnBattlefieldWithEtb();
        var theater = (Land)etb.Source;
        var startLife = _alice.LifeTotal;

        etb.Resolve();

        // Sacrifice + lifegain happen regardless of a successful search.
        _alice.Zones.Graveyard.GetCards().Should().Contain(theater);
        _alice.LifeTotal.Should().Be(startLife + 1);

        // Nonbasic dual + off-colour basic untouched (neither matches the filter).
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(plains);
        _alice.Zones.Library.GetCards().Should().Contain(dual);
        _alice.Zones.Library.GetCards().Should().Contain(plains);
    }
}
