using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MatterReshaperFactory"/>
/// (Oath of the Gatewatch, {3}{C}).
///
/// Covers:
/// - Identity (Creature — Eldrazi Drone, {3}{C}, 3/2, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Dies trigger active-zones include Graveyard (Wurmcoil posture).
/// - Resolution branches:
///   - permanent + mv <= 3 → battlefield (Creature, Land, Artifact).
///   - permanent + mv == 4 → hand (mv-too-high branch).
///   - nonpermanent (Instant) at any mv → hand.
///   - empty library → no-op.
/// </summary>
public class MatterReshaperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MatterReshaper_Identity()
    {
        var mr = MatterReshaperFactory.Create(_alice);

        mr.Name.Should().Be("Matter Reshaper");
        mr.ManaCost.Should().Be("{3}{C}");
        mr.HasType(CardType.Creature).Should().BeTrue();
        mr.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        mr.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        mr.BasePower.Should().Be(3);
        mr.BaseToughness.Should().Be(2);
        mr.Owner.Should().BeSameAs(_alice);
        mr.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MatterReshaper_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Matter Reshaper", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Matter Reshaper");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
    }

    [Fact]
    public void MatterReshaper_DiesTrigger_ActiveZonesIncludeGraveyard()
    {
        var mr = MatterReshaperFactory.Create(_alice);
        var trigger = mr.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "ActiveZones must include Graveyard so the dies-trigger guard still matches once ZoneService stamps card.Zone = Graveyard before publishing CardMovedEvent (Wurmcoil posture)");
    }

    [Fact]
    public void MatterReshaper_DiesTrigger_ConditionMatchesBattlefieldToGraveyard()
    {
        var mr = MatterReshaperFactory.Create(_alice);
        mr.SetZone(ZoneType.Battlefield);

        var trigger = mr.Abilities.OfType<TriggeredAbility>().Single();

        var dieEvent = new Majik.Core.Events.CardMovedEvent(
            mr, ZoneType.Battlefield, ZoneType.Graveyard);
        trigger.IsTriggered(dieEvent).Should().BeTrue(
            "Battlefield → Graveyard for the source matches the dies condition (CR 700.4)");

        var bounceEvent = new Majik.Core.Events.CardMovedEvent(
            mr, ZoneType.Battlefield, ZoneType.Hand);
        trigger.IsTriggered(bounceEvent).Should().BeFalse(
            "Battlefield → Hand is not a death");
    }

    [Fact]
    public void MatterReshaper_Resolve_PermanentMv3_GoesToBattlefield()
    {
        var mr = MatterReshaperFactory.Create(_alice);
        // Place a mv-3 permanent (Creature) on top of Alice's library.
        var topCreature = new Creature("Mv3 Beast", "{3}", 3, 3);
        topCreature.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCreature);
        topCreature.SetZone(ZoneType.Library);

        // Die Matter Reshaper (raw zone move to set up the trigger's
        // active-zones guard).
        _alice.Zones.Battlefield.AddCard(mr);
        mr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.RemoveCard(mr);
        _alice.Zones.Graveyard.AddCard(mr);
        mr.SetZone(ZoneType.Graveyard);

        var trigger = mr.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        topCreature.Zone.Should().Be(ZoneType.Battlefield,
            "permanent card with mv <= 3 → battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(topCreature);
        topCreature.Controller.Should().BeSameAs(_alice,
            "the new permanent enters under Matter Reshaper's controller (CR 110.2a)");
    }

    [Fact]
    public void MatterReshaper_Resolve_PermanentMv4_GoesToHand()
    {
        var mr = MatterReshaperFactory.Create(_alice);
        var topMv4 = new Creature("Mv4 Beast", "{4}", 4, 4);
        topMv4.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topMv4);
        topMv4.SetZone(ZoneType.Library);

        _alice.Zones.Battlefield.AddCard(mr);
        mr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.RemoveCard(mr);
        _alice.Zones.Graveyard.AddCard(mr);
        mr.SetZone(ZoneType.Graveyard);

        var trigger = mr.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        topMv4.Zone.Should().Be(ZoneType.Hand,
            "permanent with mv > 3 → hand");
        _alice.Zones.Hand.GetCards().Should().Contain(topMv4);
    }

    [Fact]
    public void MatterReshaper_Resolve_InstantAtAnyMv_GoesToHand()
    {
        var mr = MatterReshaperFactory.Create(_alice);
        // Mv-1 instant — fails the permanent gate even at low mv.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        _alice.Zones.Battlefield.AddCard(mr);
        mr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.RemoveCard(mr);
        _alice.Zones.Graveyard.AddCard(mr);
        mr.SetZone(ZoneType.Graveyard);

        var trigger = mr.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Hand,
            "Instant is not a permanent card (CR 110.4a) → hand regardless of mv");
        _alice.Zones.Hand.GetCards().Should().Contain(bolt);
    }

    [Fact]
    public void MatterReshaper_Resolve_LandTopOfLibrary_GoesToBattlefield()
    {
        // Land has mv 0 (no mana cost) → permanent + mv <= 3 → battlefield.
        var mr = MatterReshaperFactory.Create(_alice);
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        _alice.Zones.Battlefield.AddCard(mr);
        mr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.RemoveCard(mr);
        _alice.Zones.Graveyard.AddCard(mr);
        mr.SetZone(ZoneType.Graveyard);

        var trigger = mr.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        forest.Zone.Should().Be(ZoneType.Battlefield,
            "Land card → permanent + mv 0 → battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
    }

    [Fact]
    public void MatterReshaper_Resolve_EmptyLibrary_IsNoOp()
    {
        var mr = MatterReshaperFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(mr);
        mr.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.RemoveCard(mr);
        _alice.Zones.Graveyard.AddCard(mr);
        mr.SetZone(ZoneType.Graveyard);

        var trigger = mr.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }
}
