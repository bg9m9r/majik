using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RenegadeMapFactory"/> — Artifact {1} (Aether Revolt).
/// Oracle text (verified against Scryfall):
///   "This artifact enters tapped.
///    {T}, Sacrifice this artifact: Search your library for a basic land card,
///    reveal it, put it into your hand, then shuffle."
///
/// Covers:
/// - Identity (Artifact, {1}, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Ability shape: single <see cref="ActivatedAbility"/> with {T} + Sacrifice
///   costs (NO mana pip) and no target requests.
/// - Resolution: tutors one basic land to hand + sacrifices the map.
/// - Resolution: nonbasic in library is NOT picked; still sacrifices.
/// - Resolution: empty / no-basics library still sacrifices, nothing moved.
/// - Enters-tapped is owned by <see cref="EntersTappedBinder"/> on the
///   production load path (CR 614.1c) — this binder fires on the oracle text.
/// </summary>
public class RenegadeMapTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RenegadeMap_IsArtifact_WithOneManaCost()
    {
        var map = RenegadeMapFactory.Create(_alice);

        map.HasType(CardType.Artifact).Should().BeTrue();
        map.Name.Should().Be("Renegade Map");
        map.ManaCost.Should().Be("{1}");
        map.Owner.Should().BeSameAs(_alice);
        map.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RenegadeMap()
    {
        var card = NamedCardFactory.Create("Renegade Map", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Renegade Map");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void RenegadeMap_HasSingleActivatedAbility_NoManaAbilities()
    {
        var map = RenegadeMapFactory.Create(_alice);

        map.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        map.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Ability_HasTap_AndSacrifice_NoMana_NoTargets()
    {
        var map = RenegadeMapFactory.Create(_alice);
        var ab = map.Abilities.OfType<ActivatedAbility>().Single();

        ab.TargetRequests.Should().BeEmpty();
        ab.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Renegade Map's printed cost has no mana pip ({T}, Sacrifice only)");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap,
            "the ability costs {T}");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Sacrifice,
            "the ability sacrifices the map");
    }

    [Fact]
    public void Activate_Tutor_MovesOneBasicToHand_AndSacrificesMap()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var map = RenegadeMapFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(map);
        map.SetZone(ZoneType.Battlefield);

        var ab = map.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(forest);
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        forest.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(map,
            "the map was sacrificed as a cost");
        map.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_NonbasicNotPicked_StillSacrifices()
    {
        // Only a nonbasic land in library — must not be picked.
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var map = RenegadeMapFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(map);
        map.SetZone(ZoneType.Battlefield);

        var ab = map.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);

        _alice.Zones.Graveyard.GetCards().Should().Contain(map);
        map.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_NoBasics_StillSacrificesMap_NothingMoved()
    {
        var map = RenegadeMapFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(map);
        map.SetZone(ZoneType.Battlefield);

        var ab = map.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(map);
        map.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void EntersTappedBinder_FiresOn_RenegadeMapOracleText()
    {
        // CR 614.1c — "This artifact enters tapped." is an unconditional
        // ETB-tapped replacement, owned by EntersTappedBinder on the
        // production load path (not by this factory).
        var map = RenegadeMapFactory.Create(_alice);
        var entity = new CardEntity
        {
            Name = "Renegade Map",
            OracleText =
                "This artifact enters tapped.\n" +
                "{T}, Sacrifice this artifact: Search your library for a basic " +
                "land card, reveal it, put it into your hand, then shuffle.",
        };
        var replacements = new ReplacementBus();

        EntersTappedBinder.Bind(map, entity, replacements).Should().BeTrue(
            "the unconditional enters-tapped binder owns Renegade Map's tap-on-entry");
    }
}
