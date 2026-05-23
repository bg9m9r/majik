using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Arclight Phoenix (Guilds of Ravnica, {3}{R}).
///
/// Covers:
///   - Card identity (name, type, subtype, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the same shape.
///   - Flying + Haste keyword markers.
///   - Mechanic: Phoenix in graveyard after 3 instant casts at begin-combat
///     returns to battlefield.
///   - Mechanic: only 2 instant casts → no return.
///   - Mechanic: trigger functions from graveyard only — Phoenix on
///     battlefield must not re-fire the return effect.
///   - Mechanic: opponent's instant casts don't count toward the 3.
///   - Mechanic: TurnStartedEvent resets the per-turn count.
/// </summary>
public class ArclightPhoenixTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Burn")
    {
        var sorcery = new Sorcery(name, "1R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2);
        creature.SetOwner(controller);
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    private static void MoveToGraveyard(Creature phoenix, Player owner)
    {
        owner.Zones.Graveyard.AddCard(phoenix);
        phoenix.SetZone(ZoneType.Graveyard);
    }

    [Fact]
    public void ArclightPhoenix_Identity_Phoenix_3_2_AtCost3R_WithFlyingAndHaste()
    {
        var phoenix = ArclightPhoenixFactory.Create(_alice);

        phoenix.Name.Should().Be("Arclight Phoenix");
        phoenix.ManaCost.Should().Be("{3}{R}");
        phoenix.HasType(CardType.Creature).Should().BeTrue();
        phoenix.HasSubtype(CardSubtype.Phoenix).Should().BeTrue();
        phoenix.BasePower.Should().Be(3);
        phoenix.BaseToughness.Should().Be(2);
        phoenix.Owner.Should().BeSameAs(_alice);
        phoenix.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasFlying(phoenix).Should().BeTrue();
        CombatAbilities.HasHaste(phoenix).Should().BeTrue();
    }

    [Fact]
    public void ArclightPhoenix_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Arclight Phoenix", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Arclight Phoenix");
        card.HasSubtype(CardSubtype.Phoenix).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        CombatAbilities.HasFlying((Creature)card).Should().BeTrue();
        CombatAbilities.HasHaste((Creature)card).Should().BeTrue();
    }

    [Fact]
    public void ThreeInstantSpellsThenBeginCombat_PhoenixInGraveyard_ReturnsToBattlefield()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var phoenix = ArclightPhoenixFactory.Create(_alice, bus, triggers);
        MoveToGraveyard(phoenix, _alice);

        // Three instant casts by Alice.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S1")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S2")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S3")));

        // Begin-combat step on Alice's turn.
        bus.Publish(new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(phoenix);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(phoenix);
        phoenix.Zone.Should().Be(ZoneType.Battlefield);
        phoenix.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TwoInstantSpellsThenBeginCombat_PhoenixStaysInGraveyard()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var phoenix = ArclightPhoenixFactory.Create(_alice, bus, triggers);
        MoveToGraveyard(phoenix, _alice);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S1")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S2")));

        bus.Publish(new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice));

        // Trigger may surface (the begin-combat event matched), but the
        // intervening-if at resolve time keeps the Phoenix put.
        if (triggers.PendingCount > 0)
        {
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        _alice.Zones.Graveyard.GetCards().Should().Contain(phoenix);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(phoenix);
        phoenix.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void PhoenixOnBattlefield_TriggerFromGraveyardScope_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var phoenix = ArclightPhoenixFactory.Create(_alice, bus, triggers);
        // Phoenix is on the battlefield, NOT in the graveyard.
        _alice.Zones.Battlefield.AddCard(phoenix);
        phoenix.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S1")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S2")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S3")));

        bus.Publish(new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice));

        // Even if the trigger fires structurally, the resolve guard re-checks
        // the from-graveyard zone constraint (CR 603.6d) and no-ops. Phoenix
        // must not be added to the battlefield a second time.
        if (triggers.PendingCount > 0)
        {
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        _alice.Zones.Battlefield.GetCards()
            .Count(c => ReferenceEquals(c, phoenix)).Should().Be(1);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(phoenix);
    }

    [Fact]
    public void OpponentInstantCasts_DoNotCountTowardThree()
    {
        var bob = new Player("Bob", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var phoenix = ArclightPhoenixFactory.Create(_alice, bus, triggers);
        MoveToGraveyard(phoenix, _alice);

        // Bob casts 3 instants — must not count.
        bus.Publish(new SpellCastEvent(NewInstantSpell(bob, "B1")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(bob, "B2")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(bob, "B3")));

        // Alice begins combat.
        bus.Publish(new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice));

        if (triggers.PendingCount > 0)
        {
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        _alice.Zones.Graveyard.GetCards().Should().Contain(phoenix);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(phoenix);
    }

    [Fact]
    public void CreatureSpellsDoNotCount_OnlyInstantsAndSorceriesQualify()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var phoenix = ArclightPhoenixFactory.Create(_alice, bus, triggers);
        MoveToGraveyard(phoenix, _alice);

        // 2 instants + 1 creature spell — only 2 qualify.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S1")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "S2")));
        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Bear")));

        bus.Publish(new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice));

        if (triggers.PendingCount > 0)
        {
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        _alice.Zones.Graveyard.GetCards().Should().Contain(phoenix);

        // Now add a sorcery — total qualifying = 3. Phoenix in graveyard
        // returns at the next begin-combat.
        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Twincast")));
        bus.Publish(new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(phoenix);
    }

    [Fact]
    public void TurnBoundary_ResetsCount_NextTurnNeedsFreshThreeCasts()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var phoenix = ArclightPhoenixFactory.Create(_alice, bus, triggers);
        MoveToGraveyard(phoenix, _alice);

        // Turn 1 — cast 3 instants but no begin-combat fires (we don't pump
        // the event). The closure count is at 3 mid-turn.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "T1S1")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "T1S2")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "T1S3")));

        // Turn boundary resets the closure count.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        // Turn 2 — cast only 2 instants then begin combat. Phoenix must
        // stay in the graveyard because the count was reset to 0.
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "T2S1")));
        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "T2S2")));
        bus.Publish(new StepStartedEvent(PhaseStateType.BeginningOfCombat, _alice));

        if (triggers.PendingCount > 0)
        {
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();
        }

        _alice.Zones.Graveyard.GetCards().Should().Contain(phoenix);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(phoenix);
    }
}
