using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="NecropotenceFactory"/>.
///
/// Covers card identity, dispatcher routing, the structural skip-draw
/// + discard-exile + activated-ability shape, the skip-draw registry
/// behaviour, the discard→exile ZoneMoveIntent replacement, the
/// Pay-1-life + exile-top + delayed-end-step return-to-hand activation,
/// and multi-activation stacking. <see cref="SkipDrawRegistry.Clear"/>
/// is called in <see cref="IDisposable.Dispose"/> to prevent test
/// leakage of the process-global registry.
/// </summary>
public class NecropotenceTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public NecropotenceTests()
    {
        // Belt-and-braces — make sure earlier tests' grants don't leak in.
        SkipDrawRegistry.Clear();
    }

    public void Dispose()
    {
        // Per-test cleanup of the process-global registry.
        SkipDrawRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void Necropotence_IsEnchantment()
    {
        var necro = NecropotenceFactory.Create(_alice);
        necro.HasType(CardType.Enchantment).Should().BeTrue();
        necro.Name.Should().Be("Necropotence");
        necro.Owner.Should().BeSameAs(_alice);
        necro.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Necropotence()
    {
        var card = NamedCardFactory.Create("Necropotence", _alice);
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Name.Should().Be("Necropotence");
    }

    // -----------------------------------------------------------------------
    // Ability shape — static skip-draw, replacement discard-exile, activated
    // -----------------------------------------------------------------------

    [Fact]
    public void Necropotence_HasStaticMarkersAndActivatedAbility()
    {
        var necro = NecropotenceFactory.Create(_alice);

        var statics = necro.Abilities.OfType<StaticAbility>().ToList();
        statics.Should().HaveCount(2,
            "one marker for 'Skip your draw step', one for the discard→exile replacement");
        statics.Should().Contain(s => s.Description.Contains("Skip your draw step"));
        statics.Should().Contain(s => s.Description.Contains("discard"));
        necro.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(
            "the 'Pay 1 life: ...' activated ability");
    }

    [Fact]
    public void Necropotence_ActivatedAbility_HasPayOneLifeCost()
    {
        var necro = NecropotenceFactory.Create(_alice);
        var ability = necro.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.PayLife,
                "the activated ability's cost is 'Pay 1 life'");
    }

    // -----------------------------------------------------------------------
    // Skip draw step (structural) — SkipDrawRegistry + TurnDriver gate
    // -----------------------------------------------------------------------

    [Fact]
    public void SkipDraw_Registered_OnlyForControllerWhileOnBattlefield()
    {
        var wiring = NecropotenceFactory.Create(_alice, replacements: null, triggerManager: null);

        // Necropotence is not on the battlefield yet → skip predicate
        // returns false even for the controller (CR 614.12 — replacement
        // effects only function from the battlefield).
        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeFalse(
            "Necropotence isn't on the battlefield");
        SkipDrawRegistry.ShouldSkipDraw(_bob).Should().BeFalse(
            "Bob isn't the controller");

        // Move Necropotence to the battlefield → skip predicate now fires
        // for the controller only.
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeTrue(
            "Alice controls Necropotence on the battlefield — her draw is skipped");
        SkipDrawRegistry.ShouldSkipDraw(_bob).Should().BeFalse(
            "Necropotence does not skip the non-controller's draw step");

        // Cleanup removes the predicate.
        wiring.Cleanup.Dispose();
        SkipDrawRegistry.ShouldSkipDraw(_alice).Should().BeFalse(
            "cleanup removed the predicate");
    }

    // -----------------------------------------------------------------------
    // Discard → exile replacement
    // -----------------------------------------------------------------------

    [Fact]
    public void Discard_OfControllersCard_IsRewrittenToExile()
    {
        var bus = new ReplacementBus();
        var wiring = NecropotenceFactory.Create(_alice, replacements: bus, triggerManager: null);

        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var discarded = new Card("Discarded", "");
        discarded.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            Card: discarded,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            "Necropotence rewrites hand→graveyard moves for its controller's cards into exile");
        result.FromZone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Discard_OfOpponentsCard_IsNotReplaced()
    {
        var bus = new ReplacementBus();
        var wiring = NecropotenceFactory.Create(_alice, replacements: bus, triggerManager: null);

        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var bobsCard = new Card("Bob's card", "");
        bobsCard.SetOwner(_bob);

        var intent = new ZoneMoveIntent(
            Card: bobsCard,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Graveyard,
            Controller: _bob);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Graveyard,
            "Necropotence only exiles its controller's discards, not the opponent's");
    }

    [Fact]
    public void Discard_DoesNotReplace_WhenNecropotenceLeavesBattlefield()
    {
        var bus = new ReplacementBus();
        var wiring = NecropotenceFactory.Create(_alice, replacements: bus, triggerManager: null);

        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        // Necropotence dies → move to graveyard.
        _alice.Zones.Battlefield.RemoveCard(wiring.Card);
        _alice.Zones.Graveyard.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Graveyard);

        var discarded = new Card("Discarded", "");
        discarded.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            Card: discarded,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);

        var result = bus.Apply(intent);

        result!.ToZone.Should().Be(ZoneType.Graveyard,
            "Necropotence's replacement only functions from the battlefield (CR 614.12)");
    }

    // -----------------------------------------------------------------------
    // Activated ability: Pay 1 life, exile top, queue delayed end-step draw
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_PayLife_ExilesTopOfLibrary_AndDoesNotImmediatelyDraw()
    {
        var top = new Card("Top of library", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var necro = NecropotenceFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(necro);
        necro.SetZone(ZoneType.Battlefield);

        var startLife = _alice.LifeTotal;

        var ability = necro.Abilities.OfType<ActivatedAbility>().Single();
        var lifeCost = ability.Costs.OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.PayLife);

        lifeCost.CanPay(_alice).Should().BeTrue();
        lifeCost.Pay(_alice);

        foreach (var e in ability.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(startLife - 1, "the cost is Pay 1 life");
        _alice.Zones.Exile.GetCards().Should().Contain(top,
            "the top of the library is exiled");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        _alice.Zones.Hand.GetCards().Should().NotContain(top,
            "the exiled card is NOT drawn immediately — it waits for the next end step");
        top.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void NextEndStep_ReturnsExiledCardToHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top = new Card("Top of library", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var wiring = NecropotenceFactory.Create(_alice, replacements: null, triggerManager: triggers);
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var ability = wiring.Card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Exile, "exile happens immediately on activation");

        // Fire the next End step — the delayed trigger should be queued.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(1,
            "the delayed end-step return-to-hand is now pending");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Count.Should().Be(1);
        stack.Pop()!.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "the delayed trigger resolves to put the exiled card into the controller's hand");
        _alice.Zones.Exile.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void NonEndStep_DoesNotFireDelayedDraw()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var wiring = NecropotenceFactory.Create(_alice, replacements: null, triggerManager: triggers);
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var ability = wiring.Card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        bus.Publish(new StepStartedEvent(PhaseStateType.PreCombatMain, _alice));

        triggers.PendingCount.Should().Be(0,
            "only End-step StepStartedEvent triggers the delayed return-to-hand");
        _alice.Zones.Hand.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void MultipleActivations_Stack_AndAllReturnAtSameEndStep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var c1 = new Card("c1", "");
        var c2 = new Card("c2", "");
        var c3 = new Card("c3", "");
        _alice.Zones.Library.AddCard(c1); c1.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c2); c2.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c3); c3.SetZone(ZoneType.Library);

        var wiring = NecropotenceFactory.Create(_alice, replacements: null, triggerManager: triggers);
        _alice.Zones.Battlefield.AddCard(wiring.Card);
        wiring.Card.SetZone(ZoneType.Battlefield);

        var ability = wiring.Card.Abilities.OfType<ActivatedAbility>().Single();

        // Three activations in a row — different timestamps because
        // DateTime.UtcNow tick resolution might collide; busy-loop sleep
        // to push past the resolution if needed.
        for (int i = 0; i < 3; i++)
        {
            foreach (var e in ability.Effects) e.Execute();
            System.Threading.Thread.Sleep(1);
        }

        _alice.Zones.Exile.GetCards().Should().HaveCount(3,
            "each activation exiles one card from the top of the library");

        // Fire the End step once — every delayed trigger should fire.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().Be(3,
            "all three delayed end-step triggers are queued together");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0)
        {
            stack.Pop()!.Resolve();
        }

        _alice.Zones.Hand.GetCards().Should().HaveCount(3,
            "all three exiled cards return to hand at the same end step");
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }
}
