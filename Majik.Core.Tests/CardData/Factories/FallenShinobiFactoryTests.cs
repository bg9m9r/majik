using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Fallen Shinobi (Modern Horizons 2, {3}{U}{B}).
///
/// Oracle text (Scryfall, verified):
///   "Ninjutsu {2}{U}{B} ({2}{U}{B}, Return an unblocked attacker you control
///    to hand: Put this card onto the battlefield from your hand tapped and
///    attacking.)
///    Whenever this creature deals combat damage to a player, that player
///    exiles the top two cards of their library. Until end of turn, you may
///    play those cards without paying their mana costs."
///
/// Covers:
///   - Card identity (Creature — Zombie Ninja 5/4, {3}{U}{B}), materialised
///     from the embedded JSON definition.
///   - Ninjutsu {2}{U}{B} marker (CR 702.49).
///   - Combat-damage-to-a-player trigger structure (active on battlefield).
///   - Mechanic: damage to a player exiles the top TWO cards of their library
///     AND grants the Fallen Shinobi controller a play-without-paying
///     (ManaCost.Zero) exile grant on each, scoped to the controller (not the
///     owner).
///   - Fewer-than-two-cards edge: exiles what's there, no throw.
///   - Damage to a creature (not a player) does NOT fire the trigger.
///   - EOT cleanup clears the grants on the next Cleanup step.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "M")]
public class FallenShinobiFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FallenShinobi_Is_ZombieNinja_5_4_AtCost3UB()
    {
        var shinobi = FallenShinobiFactory.Create(_alice);

        shinobi.Name.Should().Be("Fallen Shinobi");
        shinobi.ManaCost.Should().Be("{3}{U}{B}");
        shinobi.HasType(CardType.Creature).Should().BeTrue();
        shinobi.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        shinobi.HasSubtype(CardSubtype.Ninja).Should().BeTrue();
        shinobi.BasePower.Should().Be(5);
        shinobi.BaseToughness.Should().Be(4);
        shinobi.Owner.Should().BeSameAs(_alice);
        shinobi.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FallenShinobi_HasNinjutsuMarker_At2UB()
    {
        var shinobi = FallenShinobiFactory.Create(_alice);

        // CR 702.49 — Fallen Shinobi carries a Ninjutsu {2}{U}{B} marker.
        var ninjutsu = shinobi.Abilities.OfType<NinjutsuAbility>().SingleOrDefault();
        ninjutsu.Should().NotBeNull("Fallen Shinobi has Ninjutsu");
        ninjutsu!.ManaCost.Should().Be(ManaCost.Parse("{2}{U}{B}"),
            "Fallen Shinobi's printed ninjutsu cost is {2}{U}{B}");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FallenShinobi()
    {
        var card = NamedCardFactory.Create("Fallen Shinobi", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Fallen Shinobi");
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Ninja).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(5);
        ((Creature)card).BaseToughness.Should().Be(4);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "combat-damage-to-a-player trigger is wired");
        card.Abilities.OfType<NinjutsuAbility>().Should().HaveCount(1,
            "Ninjutsu marker is wired");
    }

    [Fact]
    public void FallenShinobi_HasCombatDamageTrigger_ActiveOnBattlefieldOnly()
    {
        var shinobi = FallenShinobiFactory.Create(_alice);

        var triggers = shinobi.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var trigger = triggers[0];
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Mechanic: exile top two + grant
    // -----------------------------------------------------------------------

    [Fact]
    public void FallenShinobi_CombatDamageToPlayer_ExilesTopTwo()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        // Bob's library top two — Fallen Shinobi exiles both.
        var top1 = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        var top2 = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };
        _bob.Zones.Library.AddCard(top1); top1.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(top2); top2.SetZone(ZoneType.Library);
        // A third card stays in the library — only the top two leave.
        var deep = new Creature("Memnite", "0", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(deep); deep.SetZone(ZoneType.Library);

        var shinobi = FallenShinobiFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(shinobi);
        shinobi.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(shinobi, _bob, 5));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.Zones.Exile.GetCards().Should().Contain(new[] { top1, top2 },
            "the top two cards of the damaged player's library are exiled");
        _bob.Zones.Library.GetCards().Should().Contain(deep)
            .And.NotContain(top1).And.NotContain(top2);
    }

    [Fact]
    public void FallenShinobi_ExiledCards_ArePlayableForFree_ByController_NotOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var top1 = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        var top2 = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };
        _bob.Zones.Library.AddCard(top1); top1.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(top2); top2.SetZone(ZoneType.Library);

        var shinobi = FallenShinobiFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(shinobi);
        shinobi.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(shinobi, _bob, 5));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        foreach (var pilfered in new[] { top1, top2 })
        {
            pilfered.Zone.Should().Be(ZoneType.Exile);
            pilfered.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
                "Fallen Shinobi's controller is the named caster (not the owner)");
            pilfered.RuntimeExileCastCost.Should().NotBeNull();
            pilfered.RuntimeExileCastCost!.IsZero.Should().BeTrue(
                "\"without paying their mana costs\" ⇒ ManaCost.Zero (CR 118.9)");

            var altCost = new ExileCastAlternativeCost(
                "Fallen Shinobi: you may play that card without paying its mana cost",
                pilfered.RuntimeExileCastCost!);

            altCost.CanCastFor(pilfered, _alice).Should().BeTrue(
                "the runtime grant nominates Alice as the allowed caster");
            altCost.CanCastFor(pilfered, _bob).Should().BeFalse(
                "the permission is scoped to the Fallen Shinobi controller — " +
                "the card's owner cannot use the grant");
        }
    }

    [Fact]
    public void FallenShinobi_CombatDamageToCreature_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var topCard = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(topCard); topCard.SetZone(ZoneType.Library);

        var shinobi = FallenShinobiFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(shinobi);
        shinobi.SetZone(ZoneType.Battlefield);

        var blocker = new Creature("Bear", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(blocker); blocker.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(shinobi, blocker, 5));
        triggers.PendingCount.Should().Be(0,
            "Fallen Shinobi only triggers on combat damage to a player (CR 510 / oracle)");
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
        _bob.Zones.Library.GetCards().Should().Contain(topCard);
    }

    [Fact]
    public void FallenShinobi_FewerThanTwoCards_NoThrow_ExilesWhatExists()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var only = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        _bob.Zones.Library.AddCard(only); only.SetZone(ZoneType.Library);

        var shinobi = FallenShinobiFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(shinobi);
        shinobi.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(shinobi, _bob, 5));
        triggers.PutPendingTriggersOnStack(_alice);

        var act = () => stack.Pop()!.Resolve();
        act.Should().NotThrow(
            "exiling the top two of a one-card library exiles the one — " +
            "empty-library loss is the SBA's job, not this trigger");

        _bob.Zones.Exile.GetCards().Should().Contain(only);
        _bob.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void FallenShinobi_EOTCleanup_ClearsBothGrants()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        var top1 = new Creature("Llanowar Elves", "G", 1, 1) { Owner = _bob };
        var top2 = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };
        _bob.Zones.Library.AddCard(top1); top1.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(top2); top2.SetZone(ZoneType.Library);

        var shinobi = FallenShinobiFactory.Create(_alice, zones, bus, triggers);
        _alice.Zones.Battlefield.AddCard(shinobi);
        shinobi.SetZone(ZoneType.Battlefield);

        bus.Publish(new CombatDamageDealtEvent(shinobi, _bob, 5));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        top1.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);
        top2.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // Cleanup step fires — CR 514.2 / 514.3.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));

        top1.RuntimeExileCastAllowedCaster.Should().BeNull("EOT cleanup clears the grant");
        top2.RuntimeExileCastAllowedCaster.Should().BeNull("EOT cleanup clears the grant");
    }
}
