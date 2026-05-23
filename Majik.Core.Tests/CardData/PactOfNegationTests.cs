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
/// Unit tests for <see cref="PactOfNegationFactory"/>.
///
/// Card: Pact of Negation — Instant {0} (Future Sight).
///   "Counter target spell. At the beginning of your next upkeep,
///    pay {3}{U}{U}. If you don't, you lose the game."
///
/// Covers:
///   - Identity + dispatch + printed cost {0}.
///   - Cast at {0}: counters target spell (removes it from the stack and
///     sends its card to the graveyard).
///   - Next upkeep: controller can pay {3}{U}{U} → game continues.
///   - Next upkeep: controller cannot pay → controller is flagged as
///     having lost the game.
///   - Only the controller's upkeep triggers — opponent's upkeep does not.
/// </summary>
public class PactOfNegationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PactOfNegation_Identity()
    {
        var card = PactOfNegationFactory.Create(_alice);

        card.Name.Should().Be("Pact of Negation");
        card.ManaCost.Should().Be("{0}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PactOfNegation()
    {
        var card = NamedCardFactory.Create("Pact of Negation", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Pact of Negation");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{0}");
    }

    // -----------------------------------------------------------------------
    // Resolve: counter target spell
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_CountersTargetSpell()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        // Bob's spell on the stack (the target).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        var def = PactOfNegationFactory.BuildDefinition(
            _alice, targetResolver: o => o, stack: stack, triggers: null);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // The countered spell is no longer on the stack and its card is in
        // its owner's graveyard (CR 701.5).
        stack.Count.Should().Be(0);
        bobBolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Delayed upkeep: pay {3}{U}{U} → continue
    // -----------------------------------------------------------------------

    [Fact]
    public void NextUpkeep_ControllerCanPay_GameContinues()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Bob has a spell on stack as the counter target.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        // Resolve Pact of Negation — counters Bob's spell and queues the
        // delayed upkeep trigger.
        var def = PactOfNegationFactory.BuildDefinition(
            _alice, targetResolver: o => o, stack: stack, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard);

        // Pre-stage Alice's mana pool with {3}{U}{U} so PayMana succeeds.
        _alice.AddManaToPool(ManaCost.Parse("{3}{U}{U}"));

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

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        var def = PactOfNegationFactory.BuildDefinition(
            _alice, targetResolver: o => o, stack: stack, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Alice's mana pool is empty — PayMana({3}{U}{U}) will fail.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.HasLost.Should().BeTrue(
            "the delayed upkeep pact loses the game when {3}{U}{U} is unpaid (CR 118.3)");
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

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        var def = PactOfNegationFactory.BuildDefinition(
            _alice, targetResolver: o => o, stack: stack, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { new object[] { bobSpell } },
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
