using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ArmillarySphereFactory"/> — Artifact {2}.
/// Oracle text (verified against Scryfall):
///   "{2}, {T}, Sacrifice this artifact: Search your library for up to two
///    basic land cards, reveal them, put them into your hand, then shuffle."
///
/// Covers the card's UNIQUE behaviour vs. the analogue tutors:
/// - Identity (Artifact, {2}, owner/controller) — single *_Identity assert.
/// - Ability shape: single <see cref="ActivatedAbility"/> with {2} mana +
///   {T} + Sacrifice costs, no targets.
/// - Resolution: tutors UP TO TWO basic lands to hand (the unique "two"
///   behaviour) + sacrifices the sphere.
/// - Resolution: only one basic available -> only that one moves; still sacs.
/// - Resolution: nonbasic land is NOT picked; still sacrifices.
/// - Resolution: empty / no-basics library still sacrifices, nothing moved.
///
/// (NamedCardFactory dispatch + well-formedness are asserted globally by
/// CardFactoryContractTests — no per-card dispatch test here.)
/// </summary>
[Trait("Color", "C")]
public class ArmillarySphereFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land Basic(string name, CardSubtype sub) =>
        new(name, supertypes: new[] { CardSupertype.Basic }, subtypes: new[] { sub });

    [Fact]
    public void ArmillarySphere_Identity_ArtifactTwoMana()
    {
        var sphere = ArmillarySphereFactory.Create(_alice);

        sphere.HasType(CardType.Artifact).Should().BeTrue();
        sphere.Name.Should().Be("Armillary Sphere");
        sphere.ManaCost.Should().Be("{2}");
        sphere.Owner.Should().BeSameAs(_alice);
        sphere.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ability_HasMana_Tap_AndSacrifice_NoTargets()
    {
        var sphere = ArmillarySphereFactory.Create(_alice);

        sphere.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        var ab = sphere.Abilities.OfType<ActivatedAbility>().Single();

        ab.TargetRequests.Should().BeEmpty();
        ab.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            c => c.Description.Contains("2"),
            "Armillary Sphere's printed cost has a {2} mana pip");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap, "the ability costs {T}");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Sacrifice,
            "the ability sacrifices the sphere");
    }

    [Fact]
    public void Activate_Tutor_MovesUpToTwoBasicsToHand_AndSacrifices()
    {
        var forest = Basic("Forest", CardSubtype.Forest);
        var island = Basic("Island", CardSubtype.Island);
        foreach (var land in new[] { forest, island })
        {
            land.SetOwner(_alice);
            _alice.Zones.Library.AddCard(land);
            land.SetZone(ZoneType.Library);
        }

        var sphere = ArmillarySphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        var ab = sphere.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(forest);
        _alice.Zones.Hand.GetCards().Should().Contain(island);
        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        _alice.Zones.Library.GetCards().Should().NotContain(island);
        forest.Zone.Should().Be(ZoneType.Hand);
        island.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(sphere,
            "the sphere was sacrificed as a cost");
        sphere.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_OneBasicAvailable_MovesJustThatOne_StillSacrifices()
    {
        var forest = Basic("Forest", CardSubtype.Forest);
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var sphere = ArmillarySphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        sphere.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(forest);
        forest.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Graveyard.GetCards().Should().Contain(sphere);
        sphere.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_NonbasicNotPicked_StillSacrifices()
    {
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var sphere = ArmillarySphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        sphere.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);
        _alice.Zones.Graveyard.GetCards().Should().Contain(sphere);
        sphere.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_NoBasics_StillSacrifices_NothingMoved()
    {
        var sphere = ArmillarySphereFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sphere);
        sphere.SetZone(ZoneType.Battlefield);

        sphere.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().Contain(sphere);
        sphere.Zone.Should().Be(ZoneType.Graveyard);
    }
}
