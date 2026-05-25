using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Underworld Breach — Enchantment {1}{R}.
///   "Each nonland card in your graveyard has escape and 'Escape—[card's
///    printed mana cost], Exile three other cards from your graveyard.'
///    At the beginning of the end step, sacrifice Underworld Breach."
///
/// Validates:
///   * Card identity + dispatch.
///   * Runtime-Escape stamping on every nonland graveyard card on ETB.
///   * Lands are NOT stamped.
///   * EscapeAltCostProbe.DefaultLookup surfaces the granted escape.
///   * LTB clears the stamps.
///   * CR 500.4 / CR 603.1 — end-step sacrifice trigger fires + resolves.
/// </summary>
public class UnderworldBreachTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public UnderworldBreachTests()
    {
        _zones = new ZoneService(_bus);
    }

    // ------------------------------------------------------------------
    // Identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void UnderworldBreach_IsEnchantmentNamedUnderworldBreach_AtCost1R()
    {
        var ub = UnderworldBreachFactory.Create(_alice);

        ub.Name.Should().Be("Underworld Breach");
        ub.HasType(CardType.Enchantment).Should().BeTrue();
        ub.ManaCost.Should().Be("{1}{R}");
        ub.Owner.Should().BeSameAs(_alice);
        ub.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_UnderworldBreach()
    {
        var ub = NamedCardFactory.Create("Underworld Breach", _alice);

        ub.Should().BeOfType<Enchantment>();
        ub.Name.Should().Be("Underworld Breach");
        ub.HasType(CardType.Enchantment).Should().BeTrue();
        ub.ManaCost.Should().Be("{1}{R}");
    }

    // ------------------------------------------------------------------
    // ETB grant + Probe surface
    // ------------------------------------------------------------------

    [Fact]
    public void ApplyGraveyardGrants_StampsRuntimeEscape_OnEveryNonlandCard()
    {
        // Three nonland cards + a land in Alice's graveyard.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bears.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bears);

        var bauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _alice };
        bauble.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble);

        var land = new Land("Mountain") { Owner = _alice };
        land.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(land);

        // Sanity — nothing stamped before.
        bolt.RuntimeEscapeCost.Should().BeNull();
        bears.RuntimeEscapeCost.Should().BeNull();
        bauble.RuntimeEscapeCost.Should().BeNull();
        land.RuntimeEscapeCost.Should().BeNull();

        UnderworldBreachFactory.ApplyGraveyardGrants(_alice);

        bolt.RuntimeEscapeCost.Should().NotBeNull("Bolt is nonland");
        bolt.RuntimeEscapeCost!.TotalValue.Should().Be(1, "Bolt is {R} — mv 1");
        bolt.RuntimeEscapeExileCount.Should().Be(3,
            "Underworld Breach grants escape with 3-card exile rider");

        bears.RuntimeEscapeCost.Should().NotBeNull();
        bears.RuntimeEscapeCost!.TotalValue.Should().Be(2);
        bears.RuntimeEscapeExileCount.Should().Be(3);

        bauble.RuntimeEscapeCost.Should().NotBeNull();
        bauble.RuntimeEscapeExileCount.Should().Be(3);

        land.RuntimeEscapeCost.Should().BeNull("CR 702.143 — only nonland cards get escape");
    }

    [Fact]
    public void EscapeAltCostProbe_DefaultLookup_SurfacesUnderworldBreachGrant()
    {
        // A card that isn't on the printed-escape ship list (Bolt) gets a
        // runtime grant; the probe's DefaultLookup should surface it.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        // Pre-grant — lookup yields nothing (Bolt isn't on the ship list).
        EscapeAltCostProbe.DefaultLookup(bolt).Should().BeNull();

        UnderworldBreachFactory.ApplyGraveyardGrants(_alice);

        var descriptor = EscapeAltCostProbe.DefaultLookup(bolt);
        descriptor.Should().NotBeNull(
            "Underworld Breach's runtime grant should surface via the default probe lookup");
        descriptor!.Value.EscapeManaCost.TotalValue.Should().Be(1);
        descriptor.Value.ExileCount.Should().Be(3);
    }

    // ------------------------------------------------------------------
    // ETB+LTB lifecycle (bus-driven)
    // ------------------------------------------------------------------

    [Fact]
    public void Breach_ETB_StampsGrant_LTB_ClearsGrant()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var ub = UnderworldBreachFactory.Create(_alice, _bus, triggers: null);
        _alice.Zones.Library.AddCard(ub);
        ub.SetZone(ZoneType.Library);

        // ETB via ZoneService publish — the factory's CardMovedEvent
        // subscriber stamps the runtime escape grants.
        _zones.MoveCard(ub, ZoneType.Library, ZoneType.Battlefield, _alice);

        bolt.RuntimeEscapeCost.Should().NotBeNull(
            "Underworld Breach's ETB stamps RuntimeEscapeCost on graveyard cards");
        bolt.RuntimeEscapeExileCount.Should().Be(3);

        // LTB — move Breach off the battlefield. The subscriber clears
        // the grants.
        _zones.MoveCard(ub, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        bolt.RuntimeEscapeCost.Should().BeNull(
            "Underworld Breach's LTB clears the runtime escape grants");
        bolt.RuntimeEscapeExileCount.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // End-step sacrifice trigger (CR 500.4 / CR 603.1)
    // ------------------------------------------------------------------

    [Fact]
    public void Breach_AtControllersEndStep_SacrificesItself()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ub = UnderworldBreachFactory.Create(_alice, _bus, triggers);
        _alice.Zones.Library.AddCard(ub);
        ub.SetZone(ZoneType.Library);
        _zones.MoveCard(ub, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Fire End step on the controller's turn — the trigger should
        // queue, resolve to a sacrifice (Battlefield → Graveyard).
        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(1,
            "Underworld Breach's end-step trigger fires at the start of the controller's End step");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Count.Should().Be(1);
        stack.Pop()!.Resolve();

        ub.Zone.Should().Be(ZoneType.Graveyard,
            "the end-step trigger resolves by sacrificing Underworld Breach");
        _alice.Zones.Graveyard.GetCards().Should().Contain(ub);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(ub);
    }

    [Fact]
    public void Breach_EndStepOnOpponentsTurn_DoesNotSacrifice()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var ub = UnderworldBreachFactory.Create(_alice, _bus, triggers);
        _alice.Zones.Library.AddCard(ub);
        ub.SetZone(ZoneType.Library);
        _zones.MoveCard(ub, ZoneType.Library, ZoneType.Battlefield, _alice);

        _bus.Publish(new StepStartedEvent(PhaseStateType.End, _bob));
        _bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));

        triggers.PendingCount.Should().Be(0,
            "the trigger only fires on the controller's End step");
        ub.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Breach_OpponentGraveyardCards_AreNotStamped()
    {
        var aliceCard = new Instant("Bolt", "{R}") { Owner = _alice };
        aliceCard.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(aliceCard);

        var bobCard = new Instant("Bob's Bolt", "{R}") { Owner = _bob };
        bobCard.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobCard);

        UnderworldBreachFactory.ApplyGraveyardGrants(_alice);

        aliceCard.RuntimeEscapeCost.Should().NotBeNull(
            "Alice's graveyard card is granted escape");
        bobCard.RuntimeEscapeCost.Should().BeNull(
            "the grant is scoped to the controller's graveyard");
    }
}
