using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SlaughterPactFactory"/>.
///
/// Card: Slaughter Pact — Instant {0} (Future Sight).
///   "Destroy target nonblack creature. At the beginning of your next
///    upkeep, pay {2}{B}. If you don't, you lose the game."
///
/// Covers:
///   - Identity + dispatch + printed cost {0}.
///   - Cast at {0}: destroys a nonblack creature (moves to graveyard).
///   - Cast at {0}: does NOT destroy a black creature (illegal-target
///     filter at resolution — CR 608.2b).
///   - Next upkeep: controller can pay {2}{B} → game continues.
///   - Next upkeep: controller cannot pay → controller is flagged as
///     having lost the game.
///   - Only the controller's upkeep triggers — opponent's upkeep does not.
/// </summary>
public class SlaughterPactTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SlaughterPact_Identity()
    {
        var card = SlaughterPactFactory.Create(_alice);

        card.Name.Should().Be("Slaughter Pact");
        card.ManaCost.Should().Be("{0}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SlaughterPact()
    {
        var card = NamedCardFactory.Create("Slaughter Pact", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Slaughter Pact");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{0}");
    }

    // -----------------------------------------------------------------------
    // Resolve: destroy target nonblack creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysNonblackCreature()
    {
        // Bob has a red creature on the battlefield — nonblack, legal target.
        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var def = SlaughterPactFactory.BuildDefinition(
            _alice, targetResolver: o => o, triggers: null);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { goblin } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // CR 701.7 — destroyed creature is in its owner's graveyard.
        goblin.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
    }

    [Fact]
    public void Resolve_DoesNotDestroyBlackCreature()
    {
        // Bob has a black creature — illegal target; resolution does nothing
        // to the creature even if it slipped through to the effect.
        var dredger = new Creature("Putrid Imp", "{B}", power: 1, toughness: 1)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(dredger);
        dredger.SetZone(ZoneType.Battlefield);

        var def = SlaughterPactFactory.BuildDefinition(
            _alice, targetResolver: o => o, triggers: null);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { dredger } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Black creature is untouched (CR 608.2b illegal-target filter).
        dredger.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(dredger);
    }

    // -----------------------------------------------------------------------
    // Delayed upkeep: pay {2}{B} → continue
    // -----------------------------------------------------------------------

    [Fact]
    public void NextUpkeep_ControllerCanPay_GameContinues()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var def = SlaughterPactFactory.BuildDefinition(
            _alice, targetResolver: o => o, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { goblin } },
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        goblin.Zone.Should().Be(ZoneType.Graveyard);

        // Pre-stage Alice's mana pool with {2}{B} so PayMana succeeds.
        _alice.AddManaToPool(ManaCost.Parse("{2}{B}"));

        // Fire the next Upkeep step for Alice.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1, "the delayed upkeep pact is queued");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Count.Should().Be(1);
        stack.Pop()!.Resolve();

        _alice.HasLost.Should().BeFalse("Alice paid the pact cost in full");
    }

    // -----------------------------------------------------------------------
    // Delayed upkeep: cannot pay → controller loses
    // -----------------------------------------------------------------------

    [Fact]
    public void NextUpkeep_ControllerCannotPay_LosesTheGame()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var def = SlaughterPactFactory.BuildDefinition(
            _alice, targetResolver: o => o, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { goblin } },
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Alice's mana pool is empty — PayMana({2}{B}) will fail.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.HasLost.Should().BeTrue(
            "the delayed upkeep pact loses the game when {2}{B} is unpaid (CR 118.3)");
    }

    // -----------------------------------------------------------------------
    // Only the controller's upkeep triggers the delayed pact.
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentsUpkeep_DoesNotFireThePact()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var def = SlaughterPactFactory.BuildDefinition(
            _alice, targetResolver: o => o, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { goblin } },
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Bob's upkeep first — should NOT fire Alice's pact.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "the pact only fires on the controller's (Alice's) upkeep");
        _alice.HasLost.Should().BeFalse();

        // Now Alice's upkeep — the pact fires (and with an empty pool she
        // loses, confirming the trigger registered correctly).
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.HasLost.Should().BeTrue();
    }
}
