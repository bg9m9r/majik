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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="WayfarersBaubleFactory"/> — Artifact {1} (Time Spiral).
/// Oracle:
///   "{2}, {T}, Sacrifice Wayfarer's Bauble: Search your library for a
///    basic land card, put that card onto the battlefield tapped, then
///    shuffle."
///
/// Covers:
///   - Card identity (Artifact, {1}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: single <see cref="ActivatedAbility"/> with three
///     costs ({2}, Tap, Sacrifice) and no target requests.
///   - Resolve: tutors a basic land to battlefield tapped + sacrifices the
///     bauble.
///   - Resolve: only nonbasics in library → still sacrifices, no land moved.
/// </summary>
public class WayfarersBaubleTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WayfarersBauble_IsArtifact_AtOne()
    {
        var b = WayfarersBaubleFactory.Create(_alice);

        b.Name.Should().Be("Wayfarer's Bauble");
        b.HasType(CardType.Artifact).Should().BeTrue();
        b.ManaCost.Should().Be("{1}");
        b.Owner.Should().BeSameAs(_alice);
        b.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WayfarersBauble()
    {
        var card = NamedCardFactory.Create("Wayfarer's Bauble", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Wayfarer's Bauble");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    [Fact]
    public void Bauble_HasSingleActivatedAbility_NoManaAbilities()
    {
        var b = WayfarersBaubleFactory.Create(_alice);

        b.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        b.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Ability_Has_TwoMana_Tap_AndSacrifice_NoTargets()
    {
        var b = WayfarersBaubleFactory.Create(_alice);
        var ab = b.Abilities.OfType<ActivatedAbility>().Single();

        ab.TargetRequests.Should().BeEmpty();
        ab.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("2"),
            "the ability costs {2}");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Tap,
            "the ability requires {T}");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Sacrifice,
            "the ability sacrifices the bauble");
    }

    [Fact]
    public void Activate_Tutor_MovesBasicLandToBattlefieldTapped_AndSacrificesBauble()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var bauble = WayfarersBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ab = bauble.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.IsTapped.Should().BeTrue("the printed rider taps the tutored basic");
        forest.Zone.Should().Be(ZoneType.Battlefield);

        _alice.Zones.Graveyard.GetCards().Should().Contain(bauble,
            "the bauble was sacrificed as a cost");
        bauble.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_NonbasicLandIgnored()
    {
        // Bojuka Bog is a Land but not Basic — Wayfarer's Bauble can't tutor it.
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var bauble = WayfarersBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ab = bauble.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);

        // Cost was paid regardless.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bauble);
        bauble.Zone.Should().Be(ZoneType.Graveyard);
    }
}
