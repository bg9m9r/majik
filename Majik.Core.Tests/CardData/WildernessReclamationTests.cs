using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WildernessReclamationFactory"/> (Ravnica
/// Allegiance, {3}{G}).
///
/// Covers:
/// - Identity (name, type, mana cost, owner/controller).
/// - NamedCardFactory dispatch + Enchantment shape.
/// - End-step trigger gates on <see cref="PhaseStateType.End"/> only.
/// - "Each end step" — fires on both controller's AND opponent's end step
///   (no active-player filter).
/// - Resolution: untaps every Land the enchantment's controller controls;
///   leaves non-land permanents tapped; leaves opponent lands tapped.
/// </summary>
public class WildernessReclamationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WildernessReclamation_Identity()
    {
        var c = WildernessReclamationFactory.Create(_alice);

        c.Name.Should().Be("Wilderness Reclamation");
        c.ManaCost.Should().Be("{3}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WildernessReclamation_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Wilderness Reclamation", _alice);

        c.Should().BeOfType<Enchantment>("Wilderness Reclamation is an Enchantment");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "end-step trigger is attached");
    }

    // -----------------------------------------------------------------------
    // Trigger gating — fires only on End step
    // -----------------------------------------------------------------------

    [Fact]
    public void WildernessReclamation_Trigger_FiresOnEndStep_NotOther()
    {
        var rec = WildernessReclamationFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rec);
        rec.SetZone(ZoneType.Battlefield);

        var trigger = rec.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice))
            .Should().BeTrue("printed trigger reads 'at the beginning of each end step'");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _alice))
            .Should().BeFalse("upkeep is not the end step");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.Draw, _alice))
            .Should().BeFalse("draw is not the end step");
    }

    [Fact]
    public void WildernessReclamation_Trigger_FiresOnEachPlayersEndStep()
    {
        // Printed "each end step" has no active-player filter — fires on
        // both the controller's AND the opponent's end step.
        var rec = WildernessReclamationFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rec);
        rec.SetZone(ZoneType.Battlefield);

        var trigger = rec.Abilities.OfType<TriggeredAbility>().Single();

        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _alice))
            .Should().BeTrue("controller's own end step fires the trigger");
        trigger.IsTriggered(new StepStartedEvent(PhaseStateType.End, _bob))
            .Should().BeTrue("opponent's end step also fires the trigger ('each')");
    }

    // -----------------------------------------------------------------------
    // Resolution effect — untaps controller's lands only
    // -----------------------------------------------------------------------

    [Fact]
    public void WildernessReclamation_Resolution_UntapsControllerLands_LeavesOthers()
    {
        var rec = WildernessReclamationFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rec);
        rec.SetZone(ZoneType.Battlefield);

        // Two of Alice's lands, tapped.
        var forest = new Land("Forest");
        forest.SetOwner(_alice); forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest); forest.SetZone(ZoneType.Battlefield);
        forest.Tap();
        var island = new Land("Island");
        island.SetOwner(_alice); island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island); island.SetZone(ZoneType.Battlefield);
        island.Tap();

        // A creature Alice controls — should remain tapped.
        var bear = new Majik.Core.Cards.Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear);
        bear.Tap();

        // Bob's land — should remain tapped.
        var mountain = new Land("Mountain");
        mountain.SetOwner(_bob); mountain.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(mountain); mountain.SetZone(ZoneType.Battlefield);
        mountain.Tap();

        var trigger = rec.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        forest.IsTapped.Should().BeFalse("controller's Forest untapped");
        island.IsTapped.Should().BeFalse("controller's Island untapped");
        bear.IsTapped.Should().BeTrue("non-land creature stays tapped");
        mountain.IsTapped.Should().BeTrue("opponent's land stays tapped");
    }

    [Fact]
    public void WildernessReclamation_Resolution_AlreadyUntappedLand_NoThrow()
    {
        // Permanent.Untap() throws on already-untapped permanents; the
        // factory must guard each Untap() with an IsTapped check.
        var rec = WildernessReclamationFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(rec);
        rec.SetZone(ZoneType.Battlefield);

        var land = new Land("Forest");
        land.SetOwner(_alice); land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);
        // NOT tapped.

        var trigger = rec.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in trigger.Effects) effect.Execute();
        };

        act.Should().NotThrow("untapped lands are silently skipped");
        land.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void WildernessReclamation_OffBattlefield_NoOp()
    {
        // Zone-guard: if the enchantment isn't on the battlefield, the
        // resolution body short-circuits.
        var rec = WildernessReclamationFactory.Create(_alice);
        // Leave it in hand.
        _alice.Zones.Hand.AddCard(rec);
        rec.SetZone(ZoneType.Hand);

        var land = new Land("Forest");
        land.SetOwner(_alice); land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);
        land.Tap();

        var trigger = rec.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        land.IsTapped.Should().BeTrue(
            "off-battlefield enchantment doesn't untap anything");
    }
}
