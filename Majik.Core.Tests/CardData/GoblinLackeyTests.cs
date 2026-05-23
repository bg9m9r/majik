using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Goblin Lackey (Urza's Destiny, {R}).
///
/// Covers:
///   - Card identity (name, mana cost, P/T, Goblin subtype).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Combat-damage-to-a-player trigger structure (active on battlefield).
///   - Mechanic: combat damage to an opponent puts the first Goblin
///     creature card from the controller's hand directly onto the
///     battlefield, routed via <see cref="ZoneService.MoveCard"/>.
///   - No-op when the controller's hand has no Goblin creature card.
///   - No-op when only a non-Goblin creature is in hand (subtype-gated).
///   - Damage to a creature (not a player) does NOT fire the trigger.
/// </summary>
public class GoblinLackeyTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GoblinLackey_Is_GoblinCreature_1_1_AtCostR()
    {
        var lackey = GoblinLackeyFactory.Create(_alice);

        lackey.Name.Should().Be("Goblin Lackey");
        lackey.ManaCost.Should().Be("{R}");
        lackey.HasType(CardType.Creature).Should().BeTrue();
        lackey.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        lackey.BasePower.Should().Be(1);
        lackey.BaseToughness.Should().Be(1);
        lackey.Owner.Should().BeSameAs(_alice);
        lackey.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GoblinLackey()
    {
        var card = NamedCardFactory.Create("Goblin Lackey", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Goblin Lackey");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage-to-a-player trigger is wired");
    }

    [Fact]
    public void GoblinLackey_HasCombatDamageTrigger_ActiveOnBattlefieldOnly()
    {
        var lackey = GoblinLackeyFactory.Create(_alice);

        var triggers = lackey.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    [Fact]
    public void GoblinLackey_CombatDamageToOpponent_CheatsGoblinFromHand()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Alice's hand: a fat Goblin to cheat into play.
        var goblinPiledriver = new Creature(
            "Goblin Piledriver", "1R", 1, 2,
            subtypes: new[] { CardSubtype.Goblin })
        { Owner = _alice };
        _alice.Zones.Hand.AddCard(goblinPiledriver);
        goblinPiledriver.SetZone(ZoneType.Hand);

        var lackey = GoblinLackeyFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(lackey);
        lackey.SetZone(ZoneType.Battlefield);

        // Fire combat damage to Bob.
        bus.Publish(new CombatDamageDealtEvent(lackey, _bob, 1));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Goblin Piledriver is now on Alice's battlefield, no longer in hand.
        _alice.Zones.Battlefield.GetCards().Should().Contain(goblinPiledriver,
            "Goblin Lackey cheats a Goblin creature card from hand to battlefield");
        _alice.Zones.Hand.GetCards().Should().NotContain(goblinPiledriver);
        goblinPiledriver.Zone.Should().Be(ZoneType.Battlefield);
        goblinPiledriver.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinLackey_NoGoblinInHand_IsNoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Empty hand — the trigger should still resolve gracefully.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var lackey = GoblinLackeyFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(lackey);
        lackey.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(lackey, _bob, 1));
        triggers.PutPendingTriggersOnStack(_alice);

        var trigger = stack.Pop();
        var act = () => trigger!.Resolve();
        act.Should().NotThrow(
            "the cheat is a no-op when no Goblin creature card is in hand " +
            "(CR 117.x — \"you may\" with no valid target)");

        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(lackey,
                "only Goblin Lackey itself is on the battlefield");
    }

    [Fact]
    public void GoblinLackey_NonGoblinInHand_NotEligible()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Hand has a non-Goblin creature (Grizzly Bears) — should NOT be cheated.
        var bears = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        _alice.Zones.Hand.AddCard(bears);
        bears.SetZone(ZoneType.Hand);

        var lackey = GoblinLackeyFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(lackey);
        lackey.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(lackey, _bob, 1));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Bears stays in hand — the trigger is subtype-gated to Goblin.
        _alice.Zones.Hand.GetCards().Should().Contain(bears,
            "Goblin Lackey only cheats Goblin creature cards");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bears);
        bears.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void GoblinLackey_CombatDamageToCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Eligible Goblin in hand — but the trigger should not fire when
        // damage lands on a blocker rather than a player.
        var mogg = new Creature(
            "Mogg Fanatic", "R", 1, 1,
            subtypes: new[] { CardSubtype.Goblin })
        { Owner = _alice };
        _alice.Zones.Hand.AddCard(mogg);
        mogg.SetZone(ZoneType.Hand);

        var lackey = GoblinLackeyFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(lackey);
        lackey.SetZone(ZoneType.Battlefield);

        var blocker = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(blocker);
        blocker.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(lackey, blocker, 1));

        triggers.PendingCount.Should().Be(0,
            "Goblin Lackey only triggers on combat damage to a player " +
            "(CR 510 / oracle text)");

        // Mogg Fanatic stays in hand — no cheat occurred.
        _alice.Zones.Hand.GetCards().Should().Contain(mogg);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mogg);
    }
}
