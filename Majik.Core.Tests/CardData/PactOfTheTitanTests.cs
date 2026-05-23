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
/// Unit tests for <see cref="PactOfTheTitanFactory"/>.
///
/// Card: Pact of the Titan — Instant {0} (Future Sight).
///   "Create a 4/4 red Giant creature token. At the beginning of your
///    next upkeep, pay {4}{R}. If you don't, you lose the game."
///
/// Covers:
///   - Identity + dispatch + printed cost {0}.
///   - Cast at {0}: creates a 4/4 Giant token under the caster's control.
///   - Next upkeep: controller can pay {4}{R} → game continues.
///   - Next upkeep: controller cannot pay → controller is flagged as
///     having lost the game.
///   - Only the controller's upkeep triggers — opponent's upkeep does not.
/// </summary>
public class PactOfTheTitanTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PactOfTheTitan_Identity()
    {
        var card = PactOfTheTitanFactory.Create(_alice);

        card.Name.Should().Be("Pact of the Titan");
        card.ManaCost.Should().Be("{0}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PactOfTheTitan()
    {
        var card = NamedCardFactory.Create("Pact of the Titan", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Pact of the Titan");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{0}");
    }

    // -----------------------------------------------------------------------
    // Resolve: create a 4/4 Giant token under the caster's control
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_CreatesFourFourGiantToken()
    {
        var def = PactOfTheTitanFactory.BuildDefinition(
            _alice, triggers: null);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // CR 111 / CR 111.6 — token enters the battlefield under the caster's
        // control with the printed P/T and Giant subtype.
        var tokens = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.IsToken).ToList();
        tokens.Should().HaveCount(1, "Pact of the Titan creates exactly one token");
        var token = tokens[0];
        token.Name.Should().Be("Giant");
        token.Power.Should().Be(PactOfTheTitanFactory.TokenPower);
        token.Toughness.Should().Be(PactOfTheTitanFactory.TokenToughness);
        token.IsToken.Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
        token.HasSubtype(CardSubtype.Giant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Delayed upkeep: pay {4}{R} → continue
    // -----------------------------------------------------------------------

    [Fact]
    public void NextUpkeep_ControllerCanPay_GameContinues()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var def = PactOfTheTitanFactory.BuildDefinition(
            _alice, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Token was created.
        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Should().ContainSingle(c => c.IsToken && c.Name == "Giant");

        // Pre-stage Alice's mana pool with {4}{R} so PayMana succeeds.
        _alice.AddManaToPool(ManaCost.Parse("{4}{R}"));

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

        var def = PactOfTheTitanFactory.BuildDefinition(
            _alice, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<object[]>(),
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Alice's mana pool is empty — PayMana({4}{R}) will fail.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.HasLost.Should().BeTrue(
            "the delayed upkeep pact loses the game when {4}{R} is unpaid (CR 118.3)");
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

        var def = PactOfTheTitanFactory.BuildDefinition(
            _alice, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<object[]>(),
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
