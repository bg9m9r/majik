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
/// Unit tests for <see cref="SummonersPactFactory"/>.
///
/// Card: Summoner's Pact — Instant {0} (Future Sight).
///   "Search your library for a green creature card, reveal it, and put it
///    into your hand. Then shuffle. At the beginning of your next upkeep,
///    pay {2}{G}{G}. If you don't, you lose the game."
///
/// Covers:
///   - Identity + dispatch + printed cost {0}.
///   - Cast at {0}: tutors a green creature from library → hand;
///     non-green creatures and non-creature cards are skipped.
///   - Empty / no-green library: no-op (no crash, no card moved).
///   - Next upkeep: controller can pay {2}{G}{G} → game continues.
///   - Next upkeep: controller cannot pay → controller is flagged as
///     having lost the game.
///   - Only the controller's upkeep triggers the pact.
/// </summary>
public class SummonersPactTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreatureInLibrary(string name, string manaCost, Player owner)
    {
        var c = new Creature(name, manaCost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SummonersPact_Identity()
    {
        var card = SummonersPactFactory.Create(_alice);

        card.Name.Should().Be("Summoner's Pact");
        card.ManaCost.Should().Be("{0}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SummonersPact()
    {
        var card = NamedCardFactory.Create("Summoner's Pact", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Summoner's Pact");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{0}");
    }

    // -----------------------------------------------------------------------
    // Resolve: tutor green creature → hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_TutorsGreenCreatureToHand()
    {
        var elf = MakeCreatureInLibrary("Llanowar Elves", "{G}", _alice);
        // Distractors: non-green creature + green non-creature.
        var goblin = MakeCreatureInLibrary("Goblin Guide", "{R}", _alice);

        var def = SummonersPactFactory.BuildDefinition(_alice, triggers: null);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        elf.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(elf);
        _alice.Zones.Library.GetCards().Should().NotContain(elf);

        // Non-green creature stays in library.
        goblin.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Library.GetCards().Should().Contain(goblin);
    }

    [Fact]
    public void Resolve_DryadArborInLibrary_IsTutoredToHand()
    {
        // Regression: Dryad Arbor's color is set by a color indicator
        // (CR 202.2c), not by mana-cost pips. Before honoring the
        // indicator, Summoner's Pact silently filtered Dryad Arbor out of
        // its "green creature card" candidate list and either picked
        // nothing or skipped to the next match. Pinning the indicator path
        // here gives Summoner's Pact its own regression coverage parallel
        // to GreenSunsZenithTests.Resolve_XEquals0_TutorsDryadArborOntoBattlefield.
        var arbor = (Creature)NamedCardFactory.Create("Dryad Arbor", _alice);
        _alice.Zones.Library.AddCard(arbor);
        arbor.SetZone(ZoneType.Library);

        var def = SummonersPactFactory.BuildDefinition(_alice, triggers: null);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        arbor.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(arbor);
        _alice.Zones.Library.GetCards().Should().NotContain(arbor);
    }

    [Fact]
    public void Resolve_NoGreenCreatureInLibrary_IsNoOp()
    {
        var goblin = MakeCreatureInLibrary("Goblin Guide", "{R}", _alice);

        var def = SummonersPactFactory.BuildDefinition(_alice, triggers: null);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Nothing moves to hand.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        goblin.Zone.Should().Be(ZoneType.Library);
    }

    // -----------------------------------------------------------------------
    // Delayed upkeep: pay {2}{G}{G} → continue
    // -----------------------------------------------------------------------

    [Fact]
    public void NextUpkeep_ControllerCanPay_GameContinues()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        MakeCreatureInLibrary("Llanowar Elves", "{G}", _alice);

        var def = SummonersPactFactory.BuildDefinition(_alice, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Pre-stage Alice's mana pool with {2}{G}{G} so PayMana succeeds.
        _alice.AddManaToPool(ManaCost.Parse("{2}{G}{G}"));

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

        MakeCreatureInLibrary("Llanowar Elves", "{G}", _alice);

        var def = SummonersPactFactory.BuildDefinition(_alice, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Alice's mana pool is empty — PayMana({2}{G}{G}) will fail.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.HasLost.Should().BeTrue(
            "the delayed upkeep pact loses the game when {2}{G}{G} is unpaid (CR 118.3)");
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

        MakeCreatureInLibrary("Llanowar Elves", "{G}", _alice);

        var def = SummonersPactFactory.BuildDefinition(_alice, triggers: triggers);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
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
