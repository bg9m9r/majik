using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Questing Beast (Throne of Eldraine, {2}{G}{G}).
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Legendary, Beast).
///   - NamedCardFactory dispatch.
///   - Keyword markers: Vigilance, Deathtouch, Haste.
///   - Mechanic: "can't be blocked by creatures with power 2 or less" —
///     a power-2 blocker is illegal, a power-3 blocker is legal (CR 509.1b).
///   - Combat-damage trigger structure (active on battlefield only).
///   - Mechanic: combat damage to an opponent removes that much loyalty
///     from a planeswalker the opponent controls (CR 510 / 120.3).
///   - Combat damage to a creature (not a player) does NOT fire the trigger.
///   - No planeswalker on the opponent's battlefield → redirect is a no-op.
/// </summary>
public class QuestingBeastTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void QuestingBeast_Is_LegendaryBeast_4_4_AtCost2GG()
    {
        var qb = QuestingBeastFactory.Create(_alice);

        qb.Name.Should().Be("Questing Beast");
        qb.ManaCost.Should().Be("{2}{G}{G}");
        qb.HasType(CardType.Creature).Should().BeTrue();
        qb.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        qb.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        qb.BasePower.Should().Be(4);
        qb.BaseToughness.Should().Be(4);
        qb.Owner.Should().BeSameAs(_alice);
        qb.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_QuestingBeast()
    {
        var card = NamedCardFactory.Create("Questing Beast", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Questing Beast");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Beast).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
        ((Creature)card).BaseToughness.Should().Be(4);
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public void QuestingBeast_HasVigilanceDeathtouchHaste()
    {
        var qb = QuestingBeastFactory.Create(_alice);

        CombatAbilities.HasVigilance(qb).Should().BeTrue();
        CombatAbilities.HasDeathtouch(qb).Should().BeTrue();
        CombatAbilities.HasHaste(qb).Should().BeTrue();
    }

    [Fact]
    public void QuestingBeast_CantBeBlockedByPower2OrLess()
    {
        var effects = new ContinuousEffectsService();
        var qb = QuestingBeastFactory.Create(_alice, effects, triggers: null);

        // Power-2 blocker is illegal (CR 509.1b — "power 2 or less").
        var smallBlocker = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob };
        BlockLegality.CanBlock(smallBlocker, qb, out _).Should().BeFalse(
            "creatures with power 2 or less can't block Questing Beast");

        // Power-3 blocker is legal.
        var bigBlocker = new Creature("Centaur", "{2}{G}", 3, 3) { Owner = _bob };
        BlockLegality.CanBlock(bigBlocker, qb, out _).Should().BeTrue(
            "creatures with power 3 or more may block Questing Beast");
    }

    [Fact]
    public void QuestingBeast_HasCombatDamageTrigger_ActiveOnBattlefieldOnly()
    {
        var qb = QuestingBeastFactory.Create(_alice);

        var triggers = qb.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
        triggers[0].ActiveZones.Should().NotContain(ZoneType.Hand);
    }

    [Fact]
    public void QuestingBeast_CombatDamageToOpponent_DamagesTheirPlaneswalker()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var qb = QuestingBeastFactory.Create(_alice, effects: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(qb);
        qb.SetZone(ZoneType.Battlefield);

        // Bob controls a planeswalker with 5 starting loyalty.
        var walker = new Planeswalker("Garruk", "{3}{G}", 5) { Owner = _bob };
        walker.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(walker);
        walker.SetZone(ZoneType.Battlefield);

        // Questing Beast deals 4 combat damage to Bob.
        bus.Publish(new CombatDamageDealtEvent(qb, _bob, 4));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // CR 510 / 120.3 — that much damage redirected to Bob's planeswalker.
        walker.Loyalty.Should().Be(1, "4 combat damage removes 4 loyalty from 5");
    }

    [Fact]
    public void QuestingBeast_CombatDamageToCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var qb = QuestingBeastFactory.Create(_alice, effects: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(qb);
        qb.SetZone(ZoneType.Battlefield);

        var walker = new Planeswalker("Garruk", "{3}{G}", 5) { Owner = _bob };
        walker.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(walker);
        walker.SetZone(ZoneType.Battlefield);

        var blocker = new Creature("Wall", "{1}", 0, 4) { Owner = _bob };

        // Combat damage to a creature (not a player) — no trigger.
        bus.Publish(new CombatDamageDealtEvent(qb, blocker, 4));
        triggers.PutPendingTriggersOnStack(_alice);

        stack.IsEmpty.Should().BeTrue("damage to a creature doesn't fire the opponent trigger");
        walker.Loyalty.Should().Be(5, "the planeswalker is untouched");
    }

    [Fact]
    public void QuestingBeast_CombatDamageToOpponent_NoPlaneswalker_IsNoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var qb = QuestingBeastFactory.Create(_alice, effects: null, triggers: triggers);
        _alice.Zones.Battlefield.AddCard(qb);
        qb.SetZone(ZoneType.Battlefield);

        // Bob controls no planeswalker — redirect has no legal target.
        bus.Publish(new CombatDamageDealtEvent(qb, _bob, 4));
        triggers.PutPendingTriggersOnStack(_alice);

        // The trigger may still queue; resolving it is a no-op (no target).
        if (!stack.IsEmpty)
        {
            stack.Pop()!.Resolve();
        }

        _bob.LifeTotal.Should().Be(20, "the redirect targets a planeswalker, not the player again");
    }
}
