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
/// Tests for <see cref="SakuraTribeElderFactory"/> — Creature — Snake Shaman
/// {1}{G} 1/1 (Champions of Kamigawa). Oracle:
///   "Sacrifice this creature: Search your library for a basic land card,
///    put that card onto the battlefield tapped, then shuffle."
///
/// Covers:
///   - Card identity (Creature, {1}{G}, 1/1, Snake + Shaman, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: single <see cref="ActivatedAbility"/> with a sacrifice
///     additional cost and no target requests.
///   - Resolve: tutors a basic land to the battlefield tapped, sacrifices
///     self, leaves nonbasic lands in library.
///   - Resolve: no basics in library → still sacrifices, no land moved.
/// </summary>
public class SakuraTribeElderTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SakuraTribeElder_IsSnakeShaman_At1G_OneOne()
    {
        var c = SakuraTribeElderFactory.Create(_alice);

        c.Name.Should().Be("Sakura-Tribe Elder");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SakuraTribeElder()
    {
        var card = NamedCardFactory.Create("Sakura-Tribe Elder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sakura-Tribe Elder");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{G}");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Elder_HasSingleActivatedAbility_NoManaAbilities()
    {
        var c = SakuraTribeElderFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SacAbility_HasSacrificeCost_NoTargets_NoMana_NoTap()
    {
        var c = SakuraTribeElderFactory.Create(_alice);
        var sac = c.Abilities.OfType<ActivatedAbility>().Single();

        sac.TargetRequests.Should().BeEmpty();
        sac.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Sakura-Tribe Elder's sacrifice ability is pure sac — no mana component");
        sac.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Sacrifice,
            "the only cost is sacrificing the elder");
        sac.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Tap,
                "the printed cost has no {T} pip");
    }

    // -----------------------------------------------------------------------
    // Resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Tutor_MovesBasicLandToBattlefieldTapped_AndSacrificesSelf()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var elder = SakuraTribeElderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);

        var sac = elder.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest,
            "the tutored basic enters the battlefield");
        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeTrue("the printed rider taps the tutored basic");

        _alice.Zones.Library.GetCards().Should().NotContain(forest);

        _alice.Zones.Graveyard.GetCards().Should().Contain(elder,
            "the elder was sacrificed as a cost");
        elder.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_LeavesNonbasicLandsInLibrary()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // Nonbasic — must NOT be a legal candidate.
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var elder = SakuraTribeElderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);

        var sac = elder.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Library.GetCards().Should().Contain(bog,
            "Bojuka Bog has no Basic supertype; not a legal STE target");
    }

    [Fact]
    public void Activate_Tutor_NoBasicsInLibrary_StillSacrificesSelf_NoLandMoved()
    {
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.AddCardType(CardType.Instant);
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var elder = SakuraTribeElderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);

        var sac = elder.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bolt);
        _alice.Zones.Library.GetCards().Should().Contain(bolt);

        // Cost was paid.
        _alice.Zones.Graveyard.GetCards().Should().Contain(elder);
        elder.Zone.Should().Be(ZoneType.Graveyard);
    }
}
