using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ThroughTheBreachFactory"/>.
///
/// Through the Breach — Instant {2}{R}{R} (Champions of Kamigawa):
///   "You may put a creature card from your hand onto the battlefield.
///    That creature gains haste until end of turn. Sacrifice that
///    creature at the beginning of the next end step.
///    Splice onto Arcane {1}{R}{R}{R}." (Splice deferred.)
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve: creature card in hand → battlefield, gains Haste EOT,
///     delayed end-step sac registered.
///   - End step fires the delayed sac trigger → creature → graveyard.
///   - No creature in hand: clean no-op (no zone moves, no delayed
///     trigger registered).
/// </summary>
public class ThroughTheBreachTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public ThroughTheBreachTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ThroughTheBreach_HasExpectedShape()
    {
        var card = ThroughTheBreachFactory.Create(_alice);

        card.Name.Should().Be("Through the Breach");
        card.ManaCost.Should().Be("{2}{R}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ThroughTheBreach()
    {
        var card = NamedCardFactory.Create("Through the Breach", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Through the Breach");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve: creature card from hand → battlefield + Haste EOT + delayed sac
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_PutsCreatureFromHandToBattlefield_WithHasteEOT()
    {
        // Set up Emrakul-sized fatty in hand with a live ActiveEffects
        // service wired so the Haste grant can register.
        var continuous = new ContinuousEffectsService();
        var emrakul = new Creature("Emrakul, the Aeons Torn", "{15}", 15, 15)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Hand,
            ActiveEffects = continuous,
            HasSummoningSickness = true,
        };
        _alice.Zones.Hand.AddCard(emrakul);

        // Pre-conditions: in hand, no haste, sick.
        emrakul.Zone.Should().Be(ZoneType.Hand);
        CombatAbilities.HasHaste(emrakul).Should().BeFalse();
        emrakul.HasSummoningSickness.Should().BeTrue();

        var effect = ThroughTheBreachFactory
            .BuildResolveEffect(_alice, _zones, triggers: null)
            .Single();
        effect.Execute();

        // Creature is on the battlefield under Alice's control.
        emrakul.Zone.Should().Be(ZoneType.Battlefield,
            "Through the Breach puts the picked creature onto the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(emrakul);
        _alice.Zones.Hand.GetCards().Should().NotContain(emrakul);
        emrakul.Controller.Should().BeSameAs(_alice);

        // Haste granted until EOT (CR 702.10 / Layer 6 keyword grant).
        CombatAbilities.HasHaste(emrakul).Should().BeTrue(
            "Through the Breach grants Haste until end of turn");
        emrakul.HasSummoningSickness.Should().BeFalse(
            "Haste clears summoning sickness for attack-declaration (CR 702.10b)");

        // CR 514.2 — Haste grant expires at end of turn (cleanup).
        continuous.ExpireEndOfTurn();
        CombatAbilities.HasHaste(emrakul).Should().BeFalse(
            "Haste grant expires at end of turn");
    }

    /// <summary>
    /// Hand → Battlefield routes through ZoneService so ETB-watchers /
    /// CR 603.6a triggers on the placed creature fire. Asserted via a
    /// CardMovedEvent subscription.
    /// </summary>
    [Fact]
    public void Resolve_PutFromHand_PublishesCardMovedEvent()
    {
        var movedEvents = new List<CardMovedEvent>();
        _bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var griselbrand = new Creature("Griselbrand", "{4}{B}{B}{B}{B}", 7, 7)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Hand,
        };
        _alice.Zones.Hand.AddCard(griselbrand);

        var effect = ThroughTheBreachFactory
            .BuildResolveEffect(_alice, _zones, triggers: null)
            .Single();
        effect.Execute();

        griselbrand.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, griselbrand)
                && e.FromZone == ZoneType.Hand
                && e.ToZone == ZoneType.Battlefield,
            "hand → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // Delayed end-step sacrifice
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_RegistersDelayedEndStepSacrifice_ForPlacedCreature()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var emrakul = new Creature("Emrakul, the Aeons Torn", "{15}", 15, 15)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Hand,
        };
        _alice.Zones.Hand.AddCard(emrakul);

        var effect = ThroughTheBreachFactory
            .BuildResolveEffect(_alice, _zones, triggers)
            .Single();
        effect.Execute();

        emrakul.Zone.Should().Be(ZoneType.Battlefield,
            "creature is on the battlefield before the end step");

        // Fire the next End step — the delayed trigger should match and
        // queue itself onto the stack.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);

        // Resolve everything on the stack — the delayed trigger fires
        // its sacrifice effect.
        var resolver = new StackResolver(_bus, _zones);
        while (!stack.IsEmpty)
        {
            resolver.ResolveTop(stack);
        }

        emrakul.Zone.Should().Be(ZoneType.Graveyard,
            "CR 603.7 / CR 701.16 — delayed end-step sacrifice fires (battlefield → graveyard)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(emrakul);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(emrakul);
    }

    // -----------------------------------------------------------------------
    // No creature in hand → clean no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NoCreatureInHand_IsCleanNoOp()
    {
        // Hand contains only a non-creature — no eligible target for the
        // put-from-hand action (CR 117.x — "you may" with no valid target).
        var bolt = new Instant("Lightning Bolt", "{R}")
        {
            Owner = _alice,
            Zone = ZoneType.Hand,
        };
        _alice.Zones.Hand.AddCard(bolt);

        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var effect = ThroughTheBreachFactory
            .BuildResolveEffect(_alice, _zones, triggers)
            .Single();

        var act = () => effect.Execute();
        act.Should().NotThrow(
            "no creature card in hand → resolve is a clean no-op");

        // Non-creature stays in hand; battlefield untouched.
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();

        // No delayed trigger should have been registered (nothing to sac).
        // Stepping into the End step should not queue anything.
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().Be(0,
            "no creature placed → no delayed end-step sacrifice registered");
    }
}
