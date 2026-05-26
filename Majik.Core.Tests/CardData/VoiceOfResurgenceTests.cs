using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Voice of Resurgence (Dragon's Maze, {G}{W}, Creature —
/// Elemental 2/2).
///
/// Covers:
///   - Card identity (name, type, subtype, P/T, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Cast trigger: opponent casts a spell during controller's turn → one
///     0/1 G/W Elemental token enters under controller.
///   - Cast trigger: controller-cast spell does NOT trigger (CR 109.5 —
///     "an opponent casts").
///   - Cast trigger: opponent's spell during opponent's turn with no
///     controller-spell on the stack does NOT trigger.
///   - Dies trigger: when Voice dies, an X/X G/W Elemental enters where
///     X = creatures controller controls at resolution.
/// </summary>
public class VoiceOfResurgenceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void VoiceOfResurgence_Identity()
    {
        var v = VoiceOfResurgenceFactory.Create(_alice);

        v.Name.Should().Be("Voice of Resurgence");
        v.ManaCost.Should().Be("{G}{W}");
        v.Power.Should().Be(2);
        v.Toughness.Should().Be(2);
        v.HasType(CardType.Creature).Should().BeTrue();
        v.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        v.Owner.Should().BeSameAs(_alice);
        v.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VoiceOfResurgence()
    {
        var card = NamedCardFactory.Create("Voice of Resurgence", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Voice of Resurgence");
        card.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
    }

    [Fact]
    public void VoiceOfResurgence_HasTwoTriggeredAbilities()
    {
        var v = VoiceOfResurgenceFactory.Create(_alice);
        v.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one cast trigger + one dies trigger");
    }

    // ------------------------------------------------------------------
    // Cast trigger
    // ------------------------------------------------------------------

    [Fact]
    public void CastTrigger_Fires_WhenOpponentCastsDuringControllersTurn()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turn = new TurnManager(new List<Player> { _alice, _bob });
        turn.StartTurn(_alice);

        var voice = VoiceOfResurgenceFactory.Create(
            _alice, bus, triggers, zoneService: null,
            turnManager: turn, stack: stack);
        voice.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        // Alice's turn; Bob casts a spell.
        turn.ActivePlayer.Should().BeSameAs(_alice);
        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(1, "opponent cast on controller's turn triggers Voice");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(battlefieldBefore + 1, "one Elemental token created");
        var token = battlefield.OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Elemental").Single();
        token.BasePower.Should().Be(0);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CastTrigger_DoesNotFire_WhenControllerCastsOwnSpell()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turn = new TurnManager(new List<Player> { _alice, _bob });
        turn.StartTurn(_alice);

        var voice = VoiceOfResurgenceFactory.Create(
            _alice, bus, triggers, zoneService: null,
            turnManager: turn, stack: stack);
        voice.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Path to Exile")));

        triggers.PendingCount.Should().Be(0, "controller's own spell never triggers Voice (CR 109.5)");
    }

    [Fact]
    public void CastTrigger_DoesNotFire_WhenOpponentCastsOnOpponentsTurn_WithNoControllerSpellOnStack()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turn = new TurnManager(new List<Player> { _bob, _alice });
        turn.StartTurn(_bob); // Bob's turn first.

        var voice = VoiceOfResurgenceFactory.Create(
            _alice, bus, triggers, zoneService: null,
            turnManager: turn, stack: stack);
        voice.SetZone(ZoneType.Battlefield);

        turn.ActivePlayer.Should().BeSameAs(_bob);
        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0,
            "opponent spell on opponent's turn with no controller-spell on stack = no trigger");
    }

    // ------------------------------------------------------------------
    // Dies trigger
    // ------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_CreatesXOverXElemental_WhereXIsCreatureCount()
    {
        var alice = new Player("Alice", 20);

        // Two other creatures Alice controls at the moment Voice dies.
        var bear1 = new Creature("Bear", "1G", 2, 2);
        bear1.SetOwner(alice); bear1.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear1); bear1.SetZone(ZoneType.Battlefield);

        var bear2 = new Creature("Bear", "1G", 2, 2);
        bear2.SetOwner(alice); bear2.SetController(alice);
        alice.Zones.Battlefield.AddCard(bear2); bear2.SetZone(ZoneType.Battlefield);

        var voice = VoiceOfResurgenceFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(voice);
        voice.SetZone(ZoneType.Battlefield);

        // Voice is on the battlefield; X is currently 3 (two bears + Voice
        // itself). Move Voice to graveyard before firing the trigger
        // effect — CR 700.4 — so X reflects "creatures you control"
        // AFTER Voice has left (= 2).
        alice.Zones.Battlefield.RemoveCard(voice);
        alice.Zones.Graveyard.AddCard(voice);
        voice.SetZone(ZoneType.Graveyard);

        var dies = voice.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition.EventType == typeof(CardMovedEvent));
        foreach (var effect in dies.Effects) effect.Execute();

        // Two non-Voice creatures Alice controls + the new token. The token
        // entered as 2/2.
        var token = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Elemental");
        token.BasePower.Should().Be(2, "X = creatures Alice controls at resolution (2 bears)");
        token.BaseToughness.Should().Be(2);
        token.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        token.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void DiesTrigger_X_IsZero_WhenControllerHasNoOtherCreatures()
    {
        var alice = new Player("Alice", 20);
        var voice = VoiceOfResurgenceFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(voice);
        voice.SetZone(ZoneType.Battlefield);

        alice.Zones.Battlefield.RemoveCard(voice);
        alice.Zones.Graveyard.AddCard(voice);
        voice.SetZone(ZoneType.Graveyard);

        var dies = voice.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition.EventType == typeof(CardMovedEvent));
        foreach (var effect in dies.Effects) effect.Execute();

        var token = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Elemental");
        token.BasePower.Should().Be(0, "no other creatures Alice controls → X = 0");
        token.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void CountCreaturesControlled_OnlyCountsControllersCreatures()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var alyceBear = new Creature("Bear", "1G", 2, 2);
        alyceBear.SetOwner(alice); alyceBear.SetController(alice);
        alice.Zones.Battlefield.AddCard(alyceBear); alyceBear.SetZone(ZoneType.Battlefield);

        // A land Alice controls — not a creature, should not count.
        var land = new Land("Forest");
        land.SetOwner(alice); land.SetController(alice);
        alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);

        VoiceOfResurgenceFactory.CountCreaturesControlled(alice).Should().Be(1);
        VoiceOfResurgenceFactory.CountCreaturesControlled(bob).Should().Be(0);
    }
}
