using System.Collections.Generic;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BrineborneCutthroatFactory"/> (Throne of
/// Eldraine, {1}{U}).
///
/// Oracle text (verified against Scryfall):
///   "Flash (You may cast this spell any time you could cast an instant.)
///    Whenever you cast a spell during an opponent's turn, put a +1/+1
///    counter on this creature."
///
/// Covers:
/// - Identity (name, type, mana cost, P/T, Merfolk + Pirate subtypes,
///   Flash keyword marker, owner/controller).
/// - Cast-during-opponent's-turn trigger fires and places a +1/+1 counter;
///   P/T recomputes to 3/2 via ContinuousEffectsService's layer 7 counter
///   handler.
/// - Cast during YOUR OWN turn does NOT fire (active-player gate).
/// - Opponent casting during your turn does NOT fire (controller scope).
/// </summary>
[Trait("Color", "U")]
public class BrineborneCutthroatFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Spark")
    {
        var instant = new Instant(name, "U") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private TurnManager NewTurnManager(Player activePlayer)
    {
        var tm = new TurnManager(new List<Player> { _alice, _bob });
        tm.StartTurn(activePlayer);
        return tm;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BrineborneCutthroat_Identity_MerfolkPirate_2_1_AtCost1U()
    {
        var bc = BrineborneCutthroatFactory.Create(_alice);

        bc.Name.Should().Be("Brineborn Cutthroat");
        bc.ManaCost.Should().Be("{1}{U}");
        bc.HasType(CardType.Creature).Should().BeTrue();
        bc.HasSubtype(CardSubtype.Merfolk).Should().BeTrue(
            "Brineborn Cutthroat is a Merfolk");
        bc.HasSubtype(CardSubtype.Pirate).Should().BeTrue(
            "Brineborn Cutthroat is a Pirate");
        bc.BasePower.Should().Be(2);
        bc.BaseToughness.Should().Be(1);
        bc.Owner.Should().BeSameAs(_alice);
        bc.Controller.Should().BeSameAs(_alice);

        // Flash keyword marker (CR 702.8).
        bc.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flash",
                "Flash is wired as a KeywordAbility marker");
    }

    // -----------------------------------------------------------------------
    // Cast a spell during an OPPONENT'S turn → +1/+1 counter (2/1 → 3/2)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellCastByController_DuringOpponentsTurn_AddsPlusOnePlusOneCounter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Bob's turn — Alice's Brineborn Cutthroat is on the battlefield.
        var turns = NewTurnManager(_bob);

        var bc = BrineborneCutthroatFactory.Create(_alice, triggers, turns);
        bc.SetZone(ZoneType.Battlefield);
        bc.ActiveEffects = new ContinuousEffectsService();

        bc.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        bc.Power.Should().Be(2);
        bc.Toughness.Should().Be(1);

        // Alice casts a spell during Bob's turn → trigger fires.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Opt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        var trig = stack.Pop()!;
        trig.Resolve();

        bc.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        bc.Power.Should().Be(3, "+1/+1 counter bumps base 2/1 to 3/2");
        bc.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Cast a spell during YOUR OWN turn → no trigger (active-player gate)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellCastByController_DuringOwnTurn_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Alice's own turn.
        var turns = NewTurnManager(_alice);

        var bc = BrineborneCutthroatFactory.Create(_alice, triggers, turns);
        bc.SetZone(ZoneType.Battlefield);
        bc.ActiveEffects = new ContinuousEffectsService();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Opt")));

        triggers.PendingCount.Should().Be(0);
        bc.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        bc.Power.Should().Be(2);
        bc.Toughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Opponent casts during your turn → no trigger (controller scope)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellCastByOpponent_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Bob's turn — but Bob (the opponent) is the caster, so "you cast"
        // is not satisfied (CR 603.1 — controller-scoped).
        var turns = NewTurnManager(_bob);

        var bc = BrineborneCutthroatFactory.Create(_alice, triggers, turns);
        bc.SetZone(ZoneType.Battlefield);
        bc.ActiveEffects = new ContinuousEffectsService();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "OpponentOpt")));

        triggers.PendingCount.Should().Be(0);
        bc.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        bc.Power.Should().Be(2);
        bc.Toughness.Should().Be(1);
    }
}
