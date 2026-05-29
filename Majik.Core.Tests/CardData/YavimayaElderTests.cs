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
/// Tests for <see cref="YavimayaElderFactory"/> — Creature — Human Druid {1}{G}{G}
/// 2/1 (Weatherlight / reprints). Oracle text:
///   "When this creature dies, you may search your library for up to two basic
///    land cards, reveal them, put them into your hand, then shuffle.
///    {2}, Sacrifice this creature: Draw a card."
///
/// Covers:
///   - Card identity (Creature + Human Druid, {1}{G}, 2/1, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shapes: one <see cref="TriggeredAbility"/> (dies) + one
///     <see cref="ActivatedAbility"/> ({2} + Sacrifice, no Tap, no targets).
///   - Dies trigger resolution: tutors up to two basics to HAND + shuffles.
///   - Dies trigger: only one basic in library → tutors that one.
///   - Dies trigger: no basics → no card moved, library still shuffled.
///   - {2}, Sacrifice: Draw — draws one for the controller + sacrifices.
/// </summary>
public class YavimayaElderTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void YavimayaElder_IsCreatureHumanDruid_AtOneGreenGreen_TwoOne()
    {
        var c = YavimayaElderFactory.Create(_alice);

        c.Name.Should().Be("Yavimaya Elder");
        c.ManaCost.Should().Be("{1}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_YavimayaElder()
    {
        var card = NamedCardFactory.Create("Yavimaya Elder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Yavimaya Elder");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void Elder_HasOneTriggered_AndOneActivated_NoManaAbilities()
    {
        var c = YavimayaElderFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SacDrawAbility_HasTwoMana_AndSacrifice_NoTap_NoTargets()
    {
        var c = YavimayaElderFactory.Create(_alice);
        var ab = c.Abilities.OfType<ActivatedAbility>().Single();

        ab.TargetRequests.Should().BeEmpty();
        ab.Costs.OfType<ManaCostCost>().Should().ContainSingle(c => c.Description.Contains("2"),
            "the ability costs {2}");
        ab.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Sacrifice,
            "the ability sacrifices the elder");
        ab.Costs.OfType<AdditionalCost>().Should().NotContain(
            c => c.CostType == AdditionalCostType.Tap,
            "Yavimaya Elder's printed sac-draw cost has no {T} pip");
    }

    [Fact]
    public void Dies_Tutor_MovesTwoBasicsToHand_AndShuffles()
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

        var elder = YavimayaElderFactory.Create(_alice);
        var dies = elder.Abilities.OfType<TriggeredAbility>().Single();
        dies.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(forest);
        _alice.Zones.Hand.GetCards().Should().Contain(island);
        forest.Zone.Should().Be(ZoneType.Hand);
        island.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Library.GetCards().Should().NotContain(forest);
        _alice.Zones.Library.GetCards().Should().NotContain(island);
    }

    [Fact]
    public void Dies_Tutor_OnlyOneBasicInLibrary_StillTutorsThatOne()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var elder = YavimayaElderFactory.Create(_alice);
        var dies = elder.Abilities.OfType<TriggeredAbility>().Single();
        dies.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(forest);
        forest.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Dies_Tutor_NoBasics_NoCardMoved()
    {
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var elder = YavimayaElderFactory.Create(_alice);
        var dies = elder.Abilities.OfType<TriggeredAbility>().Single();
        dies.Resolve();

        _alice.Zones.Hand.GetCards().Should().NotContain(bog);
        _alice.Zones.Library.GetCards().Should().Contain(bog);
    }

    [Fact]
    public void Activate_SacDraw_DrawsACard_AndSacrificesElder()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var elder = YavimayaElderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);

        var draw = elder.Abilities.OfType<ActivatedAbility>().Single();
        draw.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);

        _alice.Zones.Graveyard.GetCards().Should().Contain(elder,
            "the elder was sacrificed as a cost");
        elder.Zone.Should().Be(ZoneType.Graveyard);
    }
}
