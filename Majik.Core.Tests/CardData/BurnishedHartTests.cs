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
/// Tests for <see cref="BurnishedHartFactory"/> — Artifact Creature — Elk {3}
/// 2/2 (Theros). Oracle:
///   "{3}, Sacrifice this creature: Search your library for up to two basic
///    land cards, put them onto the battlefield tapped, then shuffle."
///
/// Covers:
///   - Card identity (Artifact + Creature + Elk, {3}, 2/2, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: single <see cref="ActivatedAbility"/> with two costs
///     ({3} + Sacrifice; NO Tap) and no target requests.
///   - Resolve: tutors two basic lands to battlefield tapped + sacrifices the
///     hart.
///   - Resolve: only one basic in library → tutors that one + sacrifices.
///   - Resolve: no basics in library → still sacrifices, no land moved.
/// </summary>
public class BurnishedHartTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BurnishedHart_IsArtifactCreatureElk_AtThree_TwoTwo()
    {
        var c = BurnishedHartFactory.Create(_alice);

        c.Name.Should().Be("Burnished Hart");
        c.ManaCost.Should().Be("{3}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Burnished Hart is BOTH Artifact and Creature (CR 205.2a)");
        c.HasSubtype(CardSubtype.Elk).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BurnishedHart()
    {
        var card = NamedCardFactory.Create("Burnished Hart", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Burnished Hart");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void Hart_HasSingleActivatedAbility_NoManaAbilities()
    {
        var c = BurnishedHartFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Ability_HasThreeMana_AndSacrifice_NoTap_NoTargets()
    {
        var c = BurnishedHartFactory.Create(_alice);
        var ab = c.Abilities.OfType<ActivatedAbility>().Single();

        ab.TargetRequests.Should().BeEmpty();
        ab.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("3"),
            "the ability costs {3}");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Sacrifice,
            "the ability sacrifices the hart");
        ab.Costs.OfType<AdditionalCost>().Should().NotContain(
            c => c.CostType == AdditionalCostType.Tap,
            "Burnished Hart's printed cost has no {T} pip");
    }

    [Fact]
    public void Activate_Tutor_MovesTwoBasicsToBattlefieldTapped_AndSacrificesHart()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var island = new Land("Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        _alice.Zones.Library.AddCard(island);
        island.SetZone(ZoneType.Library);

        var hart = BurnishedHartFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hart);
        hart.SetZone(ZoneType.Battlefield);

        var ab = hart.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Battlefield.GetCards().Should().Contain(island);
        forest.IsTapped.Should().BeTrue();
        island.IsTapped.Should().BeTrue();
        forest.Zone.Should().Be(ZoneType.Battlefield);
        island.Zone.Should().Be(ZoneType.Battlefield);

        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        _alice.Zones.Library.GetCards().Should().NotContain(island);

        _alice.Zones.Graveyard.GetCards().Should().Contain(hart,
            "the hart was sacrificed as a cost");
        hart.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_OnlyOneBasicInLibrary_StillTutorsThatOne()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var hart = BurnishedHartFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hart);
        hart.SetZone(ZoneType.Battlefield);

        var ab = hart.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.IsTapped.Should().BeTrue();
        _alice.Zones.Graveyard.GetCards().Should().Contain(hart);
    }

    [Fact]
    public void Activate_Tutor_NoBasics_StillSacrificesHart_NoLandMoved()
    {
        // Only a nonbasic in library — must not be picked.
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var hart = BurnishedHartFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(hart);
        hart.SetZone(ZoneType.Battlefield);

        var ab = hart.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);

        _alice.Zones.Graveyard.GetCards().Should().Contain(hart);
        hart.Zone.Should().Be(ZoneType.Graveyard);
    }
}
