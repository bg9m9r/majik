using System;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Slickshot Lockpicker (Outlaws of Thunder Junction, {2}{U},
/// Creature — Human Rogue 2/3).
///
/// Covers the card's UNIQUE behaviour (its Snapcaster-style ETB flashback
/// grant) plus a single identity assert:
///   - Identity: Creature — Human Rogue 2/3 at {2}{U}.
///   - ETB is a single mandatory 1..1 targeted trigger for an instant/sorcery
///     card in the controller's graveyard.
///   - On resolution the chosen graveyard instant/sorcery gains runtime
///     flashback at its own printed mana cost (CR 702.34).
///   - Illegal-on-resolution recheck: a non-instant/sorcery (or a card no
///     longer in the graveyard) is not granted flashback (CR 603.10b).
///   - The grant expires on the next Cleanup step when a bus is wired
///     (CR 514.2).
///   - Plot (CR 718) is deferred — no activated-from-hand ability is wired.
///
/// (Dispatch + well-formedness are asserted for every implemented card by
/// CardFactoryContractTests, so no dispatch test is duplicated here.)
/// </summary>
[Trait("Color", "U")]
public class SlickshotLockpickerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Instant GraveInstant(string name, string cost)
    {
        var c = new Instant(name, cost) { Owner = _alice };
        c.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(c);
        return c;
    }

    private static void ResolveEtbWithTarget(Creature card, Card? target)
    {
        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        if (target != null)
        {
            etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        }
        foreach (var e in etb.Effects) e.Execute();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SlickshotLockpicker_Identity_HumanRogue_2_3_AtCost2U()
    {
        var card = SlickshotLockpickerFactory.Create(_alice);

        card.Name.Should().Be("Slickshot Lockpicker");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // ETB structure
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_IsSingleMandatoryTargetedTrigger_ForGraveyardInstantOrSorcery()
    {
        var card = SlickshotLockpickerFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var req = triggers[0].TargetRequests.Should().ContainSingle().Subject;
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("instant or sorcery");
        req.Description.Should().Contain("graveyard");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // ETB flashback grant
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_GrantsFlashback_AtTargetsOwnManaCost()
    {
        var card = SlickshotLockpickerFactory.Create(_alice);
        var bolt = GraveInstant("Lightning Bolt", "{R}");

        ResolveEtbWithTarget(card, bolt);

        bolt.RuntimeFlashbackCost.Should().NotBeNull(
            "the chosen graveyard instant gains flashback (CR 702.34)");
        bolt.RuntimeFlashbackCost!.TotalValue.Should().Be(1,
            "the flashback cost is equal to its mana cost ({R})");
    }

    [Fact]
    public void Etb_GrantsFlashback_ForGraveyardSorcery_AtItsManaCost()
    {
        var card = SlickshotLockpickerFactory.Create(_alice);
        var divination = new Sorcery("Divination", "{2}{U}") { Owner = _alice };
        divination.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(divination);

        ResolveEtbWithTarget(card, divination);

        divination.RuntimeFlashbackCost.Should().NotBeNull();
        divination.RuntimeFlashbackCost!.TotalValue.Should().Be(3);
    }

    [Fact]
    public void Etb_DoesNotGrantFlashback_ToNonInstantOrSorcery()
    {
        var card = SlickshotLockpickerFactory.Create(_alice);
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bears.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bears);

        ResolveEtbWithTarget(card, bears);

        bears.RuntimeFlashbackCost.Should().BeNull(
            "CR 603.10b — only an instant or sorcery card is a legal target on resolution");
    }

    [Fact]
    public void Etb_NoTargetChosen_IsCleanNoOp()
    {
        var card = SlickshotLockpickerFactory.Create(_alice);
        var bolt = GraveInstant("Lightning Bolt", "{R}");

        ResolveEtbWithTarget(card, target: null);

        bolt.RuntimeFlashbackCost.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // End-of-turn cleanup (CR 514.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void GrantedFlashback_ExpiresAtEndOfTurn_WhenBusWired()
    {
        var bus = new EventBus();
        var card = SlickshotLockpickerFactory.Create(_alice, bus);
        var bolt = GraveInstant("Lightning Bolt", "{R}");

        ResolveEtbWithTarget(card, bolt);
        bolt.RuntimeFlashbackCost.Should().NotBeNull("grant is live before EOT");

        bus.Publish(new StepStartedEvent(StepStateType.Cleanup, _alice));

        bolt.RuntimeFlashbackCost.Should().BeNull(
            "CR 514.2 — the runtime flashback grant expires at end of turn");
    }

    // -----------------------------------------------------------------------
    // Plot deferral guardrail (CR 718) — no activated-from-hand ability wired.
    // Pins the gap so future Plot wiring is observable as a behavioral change.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plot_IsDeferred_NoActivatedAbilityFromHand()
    {
        var card = SlickshotLockpickerFactory.Create(_alice);

        card.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Plot (CR 718) is deferred until the cast-from-exile-on-a-later-turn primitive lands");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
}
