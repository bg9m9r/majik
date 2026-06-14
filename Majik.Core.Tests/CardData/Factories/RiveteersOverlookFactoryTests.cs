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
/// Tests for <see cref="RiveteersOverlookFactory"/> (Streets of New Capenna —
/// the Jund member of the common "tapped triome fetch" land cycle).
///
/// Oracle (verified against Scryfall 2026-06-14):
///   <c>When this land enters, sacrifice it. When you do, search your library
///      for a basic Swamp, Mountain, or Forest card, put it onto the
///      battlefield tapped, then shuffle and you gain 1 life.</c>
///
/// The card is a colorless nonbasic land (no produced mana of its own). The
/// unique behaviour is the ETB-triggered self-sacrifice + reflexive tutor of a
/// basic Swamp / Mountain / Forest onto the battlefield tapped (CR 205.4a /
/// CR 701.19a) with the printed "shuffle, then gain 1 life" rider (CR 701.20a /
/// CR 119.3). The reflexive "When you do" sub-trigger is modelled as part of the
/// single ETB triggered ability — for this non-targeting, non-optional payoff it
/// resolves as one practical unit (CR 603.2g — reflexive trigger).
/// </summary>
[Trait("Color", "C")]
public class RiveteersOverlookFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_ProducesNonbasicLand_NoSupertypeNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Riveteers Overlook", _alice);

        land.Name.Should().Be("Riveteers Overlook");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("triome fetch lands are nonbasic");
        land.Subtypes.Should().BeEmpty();
    }

    [Fact]
    public void HasEtbTriggeredAbility()
    {
        var land = (Land)NamedCardFactory.Create("Riveteers Overlook", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the only printed ability is the ETB sacrifice + reflexive fetch");
    }

    // -----------------------------------------------------------------------
    // ETB: sacrifice self, fetch a basic Swamp/Mountain/Forest tapped, gain 1.
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbResolve_SacrificesSelf_FetchesBasicSwampTapped_AndGainsOneLife()
    {
        var basicSwamp = new Land(
            "Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        // A basic Plains is NOT a legal target (only Swamp/Mountain/Forest).
        var basicPlains = new Land(
            "Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        _alice.Zones.Library.AddCard(basicSwamp);
        _alice.Zones.Library.AddCard(basicPlains);
        basicSwamp.SetZone(ZoneType.Library);
        basicPlains.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Riveteers Overlook", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in etb.Effects) eff.Execute();

        // Basic Swamp fetched to battlefield tapped; off-color Plains untouched.
        _alice.Zones.Battlefield.GetCards().Should().Contain(basicSwamp);
        basicSwamp.IsTapped.Should().BeTrue("put onto the battlefield tapped");
        _alice.Zones.Library.GetCards().Should().Contain(basicPlains,
            "Plains is not a Swamp/Mountain/Forest");
        _alice.Zones.Library.GetCards().Should().NotContain(basicSwamp);

        // Riveteers Overlook sacrificed itself.
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);

        // "and you gain 1 life" (CR 119.3).
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void EtbResolve_FetchesBasicMountain_AndBasicForest_AreLegalTargets()
    {
        var basicMountain = new Land(
            "Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        var basicForest = new Land(
            "Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        _alice.Zones.Library.AddCard(basicMountain);
        _alice.Zones.Library.AddCard(basicForest);
        basicMountain.SetZone(ZoneType.Library);
        basicForest.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Riveteers Overlook", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in etb.Effects) eff.Execute();

        // One of the two legal basics was fetched (deterministic first match),
        // tapped, and the other stayed in the library.
        var battlefieldBasics = _alice.Zones.Battlefield.GetCards()
            .Where(c => c.HasSupertype(CardSupertype.Basic)).ToList();
        battlefieldBasics.Should().ContainSingle();
        battlefieldBasics[0].Should().Match<ICard>(
            c => c == basicMountain || c == basicForest);
        ((Permanent)battlefieldBasics[0]).IsTapped.Should().BeTrue();
        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void EtbResolve_NoLegalBasic_StillSacrificesAndGainsLife()
    {
        // Only a nonbasic dual in library — search finds nothing, but the
        // sacrifice + lifegain still happen (CR 701.20a shuffle / CR 119.3).
        var dual = new Land(
            "Blood Crypt", supertypes: null,
            new[] { CardSubtype.Swamp, CardSubtype.Mountain });
        _alice.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var land = (Land)NamedCardFactory.Create("Riveteers Overlook", _alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var eff in etb.Effects) eff.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
        // Nonbasic untouched (only BASIC Swamp/Mountain/Forest are legal).
        _alice.Zones.Library.GetCards().Should().Contain(dual);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(dual);
        _alice.LifeTotal.Should().Be(21);
    }
}
