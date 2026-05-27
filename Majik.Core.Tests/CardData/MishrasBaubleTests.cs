using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MishrasBaubleFactory"/>.
///
/// Covers card identity, the {T} + Sacrifice activated ability shape, the
/// look-at-top no-op semantics, the sacrifice side effect, and the delayed
/// upkeep draw triggered ability registered via
/// <see cref="DelayedTriggeredAbility"/>.
/// </summary>
public class MishrasBaubleTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MishrasBauble_IsArtifact()
    {
        var bauble = MishrasBaubleFactory.Create(_alice);
        bauble.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void MishrasBauble_NameIsCorrect()
    {
        var bauble = MishrasBaubleFactory.Create(_alice);
        bauble.Name.Should().Be("Mishra's Bauble");
    }

    [Fact]
    public void MishrasBauble_OwnerAndControllerAreSet()
    {
        var bauble = MishrasBaubleFactory.Create(_alice);
        bauble.Owner.Should().BeSameAs(_alice);
        bauble.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MishrasBauble_HasExactlyOneActivatedAbility()
    {
        var bauble = MishrasBaubleFactory.Create(_alice);
        bauble.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void MishrasBauble_Ability_HasTapAndSacrificeCosts()
    {
        var bauble = MishrasBaubleFactory.Create(_alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap, "the {T} cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice, "the sac cost");
    }

    // -----------------------------------------------------------------------
    // Activation: look-at-top is a no-op, sacrifice moves the bauble to GY
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_LookAtTop_DoesNotMoveLibraryCard()
    {
        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bauble = MishrasBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().Contain(top,
            "look-at-top is information-only — the card stays on top of the library");
        _alice.Zones.Hand.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void Activation_SacrificesBauble_MovesToGraveyard()
    {
        var bauble = MishrasBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(bauble,
            "sacrifice moves the bauble to its owner's graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bauble);
        bauble.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Delayed trigger: next upkeep -> draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_WithTriggerManager_RegistersDelayedDrawTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var bauble = MishrasBaubleFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Delayed trigger sits dormant until an Upkeep StepStarted is seen.
        triggers.PendingCount.Should().Be(0, "no upkeep event has fired yet");
    }

    [Fact]
    public void NextUpkeepStepStarted_DrawsACard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top = new Card("Top of library", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bauble = MishrasBaubleFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // Fire the next upkeep — the active player here is Bob, but the
        // delayed trigger doesn't filter on whose upkeep it is; it fires on
        // the first Upkeep StepStartedEvent after activation.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));

        triggers.PendingCount.Should().Be(1, "the delayed draw is now pending");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Count.Should().Be(1);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "the delayed trigger resolves to draw a card");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void NonUpkeepStep_DoesNotFireDelayedDraw()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bauble = MishrasBaubleFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(0, "only Upkeep steps trigger the delayed draw");
        _alice.Zones.Hand.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void DelayedDrawTrigger_OnlyFiresOnce()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card1 = new Card("c1", "");
        var card2 = new Card("c2", "");
        _alice.Zones.Library.AddCard(card1); card1.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(card2); card2.SetZone(ZoneType.Library);

        var bauble = MishrasBaubleFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        // First upkeep fires + resolves + auto-unregisters.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Subsequent upkeeps should not re-fire it (delayed = one-shot per
        // CR 603.7d / TriggerManager.EvaluateTriggers self-removal).
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(0,
            "delayed triggered abilities auto-unregister after firing once");
        _alice.Zones.Hand.GetCards().Should().ContainSingle();
    }
}
