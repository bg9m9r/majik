using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LunarConvocationFactory"/> (Murders at Karlov
/// Manor Commander, {W}{B}). Enchantment.
///
/// Covers ONLY the card's unique behaviour:
/// - Identity: Enchantment, {W}{B}, two end-step triggers + one activated
///   ability.
/// - First end-step trigger (CR 603.4): each opponent loses 1 life iff the
///   controller gained life this turn — fires when life was gained, no-ops
///   otherwise.
/// - Second end-step trigger (CR 603.4): create a 1/1 black Bat with flying
///   iff the controller gained AND lost life this turn.
/// - "{1}{B}, Pay 2 life: Draw a card" activated ability shape (costs +
///   draw-on-resolve).
/// </summary>
[Trait("Color", "M")]
public class LunarConvocationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LunarConvocation_Identity()
    {
        var c = LunarConvocationFactory.Create(_alice);

        c.Name.Should().Be("Lunar Convocation");
        c.ManaCost.Should().Be("{W}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "two 'at the beginning of your end step' triggers");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {1}{B}, Pay 2 life: Draw a card ability");
    }

    [Fact]
    public void DrawAbility_HasManaAndPayLifeCosts()
    {
        var c = LunarConvocationFactory.Create(_alice);

        var draw = c.Abilities.OfType<ActivatedAbility>().Single();
        draw.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        draw.Costs.OfType<PayLifeCost>().Single().Amount.Should().Be(2,
            "the printed cost is {1}{B}, Pay 2 life");
    }

    [Fact]
    public void DrainTrigger_GainedLifeThisTurn_EachOpponentLoses1()
    {
        var bus = new EventBus();
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(bus), bus);
        var convocation = LunarConvocationFactory.Create(
            _alice, zoneService: null, eventBus: bus, triggers: triggers);

        _alice.Zones.Battlefield.AddCard(convocation);
        convocation.SetZone(ZoneType.Battlefield);

        // Alice gains life this turn (latch stamped via LifeChangedEvent).
        bus.Publish(new LifeChangedEvent(_alice, previousLife: 20, newLife: 23));

        // The first end-step trigger is the drain; identify by resolving each
        // and asserting the opponent-loss outcome.
        var endStep = new StepStartedEvent(StepStateType.End, _alice);
        var fired = convocation.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(endStep)).ToList();
        fired.Should().HaveCount(2, "both end-step triggers fire on Alice's end step");

        foreach (var t in fired) ResolveWithGame(t, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life (gained life this turn)");
    }

    [Fact]
    public void DrainTrigger_NoLifeGained_DoesNotDrain()
    {
        var bus = new EventBus();
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(bus), bus);
        var convocation = LunarConvocationFactory.Create(
            _alice, zoneService: null, eventBus: bus, triggers: triggers);

        _alice.Zones.Battlefield.AddCard(convocation);
        convocation.SetZone(ZoneType.Battlefield);

        // No life gained this turn — the intervening-if (CR 603.4) is false.
        var endStep = new StepStartedEvent(StepStateType.End, _alice);
        var fired = convocation.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(endStep)).ToList();

        foreach (var t in fired) ResolveWithGame(t, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(20, "no life gained ⇒ no drain");
    }

    [Fact]
    public void EndStepTrigger_OpponentsEndStep_DoesNotFire()
    {
        var convocation = LunarConvocationFactory.Create(_alice);

        // "your end step" — controller-filtered (CR 500.7). Bob's end step
        // must not trigger Alice's enchantment.
        var bobEndStep = new StepStartedEvent(StepStateType.End, _bob);
        convocation.Abilities.OfType<TriggeredAbility>()
            .Any(t => t.IsTriggered(bobEndStep)).Should().BeFalse(
                "'your end step' filters out the opponent's end step");
    }

    [Fact]
    public void BatTrigger_GainedAndLostLife_CreatesBatToken()
    {
        var bus = new EventBus();
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(bus), bus);
        var convocation = LunarConvocationFactory.Create(
            _alice, zoneService: null, eventBus: bus, triggers: triggers);

        _alice.Zones.Battlefield.AddCard(convocation);
        convocation.SetZone(ZoneType.Battlefield);

        // Gained AND lost life this turn (CR 603.4 — both required).
        bus.Publish(new LifeChangedEvent(_alice, previousLife: 20, newLife: 23));
        _alice.LoseLife(2); // pays the bat trigger's "lost life" condition

        var before = _alice.Zones.Battlefield.GetCards().OfType<Creature>().Count();

        var endStep = new StepStartedEvent(StepStateType.End, _alice);
        var fired = convocation.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(endStep)).ToList();
        foreach (var t in fired) ResolveWithGame(t, _alice, _alice, _bob);

        var bats = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Where(c => c.Name == "Bat").ToList();
        bats.Should().ContainSingle("gained and lost life ⇒ one 1/1 black Bat token");
        var bat = bats.Single();
        bat.BasePower.Should().Be(1);
        bat.BaseToughness.Should().Be(1);
        bat.Subtypes.Should().Contain(CardSubtype.Bat);
        bat.Abilities.OfType<KeywordAbility>().Any(k => k.Keyword == "Flying")
            .Should().BeTrue("the Bat token has flying");
        (_alice.Zones.Battlefield.GetCards().OfType<Creature>().Count() - before)
            .Should().Be(1);
    }

    [Fact]
    public void BatTrigger_GainedButNotLostLife_NoToken()
    {
        var bus = new EventBus();
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(bus), bus);
        var convocation = LunarConvocationFactory.Create(
            _alice, zoneService: null, eventBus: bus, triggers: triggers);

        _alice.Zones.Battlefield.AddCard(convocation);
        convocation.SetZone(ZoneType.Battlefield);

        // Gained life but did NOT lose life — the bat intervening-if is false.
        bus.Publish(new LifeChangedEvent(_alice, previousLife: 20, newLife: 23));

        var endStep = new StepStartedEvent(StepStateType.End, _alice);
        var fired = convocation.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.IsTriggered(endStep)).ToList();
        foreach (var t in fired) ResolveWithGame(t, _alice, _alice, _bob);

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Any(c => c.Name == "Bat").Should().BeFalse(
                "gained but did not lose life ⇒ no Bat token");
    }

    private static void ResolveWithGame(
        TriggeredAbility trigger, Player controller, params Player[] players)
    {
        var game = new GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));

        trigger.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }
}
