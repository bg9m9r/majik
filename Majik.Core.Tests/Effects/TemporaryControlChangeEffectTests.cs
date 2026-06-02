using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// Engine-level coverage for the Threaten / Act of Treason primitive
/// (<see cref="TemporaryControlChangeEffect"/>, CR 613.2 + CR 514.2). A
/// temporary control swap must (a) actually change
/// <see cref="Majik.Core.Cards.Permanent.Controller"/> so combat / priority
/// honour it, and (b) revert to the prior controller at the cleanup step.
/// </summary>
public class TemporaryControlChangeEffectTests
{
    [Fact]
    public void Register_SwapsRealController_Immediately()
    {
        var continuous = new ContinuousEffectsService();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        bob.Zones.Battlefield.AddCard(bear);

        continuous.Register(new TemporaryControlChangeEffect(bear, alice));

        bear.Controller.Should().BeSameAs(alice,
            "CR 613.2 — temporary control change swaps the real controller so combat honours it");
    }

    [Fact]
    public void ExpireEndOfTurn_RestoresPriorController()
    {
        var continuous = new ContinuousEffectsService();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        bob.Zones.Battlefield.AddCard(bear);

        continuous.Register(new TemporaryControlChangeEffect(bear, alice));
        bear.Controller.Should().BeSameAs(alice);

        continuous.ExpireEndOfTurn();

        bear.Controller.Should().BeSameAs(bob,
            "CR 514.2 — control reverts to the owner/prior controller at the cleanup step");
    }

    [Fact]
    public async Task StolenCreature_CanAttackWithHaste_ThenRevertsAtCleanup()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new Majik.Core.Abilities.TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var continuous = new ContinuousEffectsService(bus);
        var combat = new CombatFlow(bus, sba);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        for (var i = 0; i < 5; i++)
        {
            var c = NamedCardFactory.Create("Mountain", alice);
            alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library);
        }

        // Bob's tapped, summoning-sick bear — Alice will Threaten it.
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        bob.Zones.Battlefield.AddCard(bear);
        bear.Tap();
        bear.HasSummoningSickness = true; // control gained this turn ⇒ sick (CR 302.6)

        // Threaten template: gain control until EOT + untap + haste until EOT.
        continuous.Register(new TemporaryControlChangeEffect(bear, alice));
        bear.Untap();
        continuous.Register(new GrantKeywordUntilEndOfTurnEffect(bear, "Haste"));

        // Mid-turn the stolen bear is Alice's and combat-ready.
        bear.Controller.Should().BeSameAs(alice);
        bear.IsTapped.Should().BeFalse("Threaten untaps the stolen creature");
        CombatAbilities.HasHaste(bear).Should().BeTrue("Threaten grants haste until end of turn");

        // CR 508.1a — attacker validation reads the real controller + haste.
        var validator = new CombatValidator();
        validator.CanAttack(bear, alice).Should().BeTrue(
            "the stolen creature attacks for its new controller this turn (untapped + haste)");

        var players = new List<Player> { alice, bob };
        var priorityMgr = new PriorityManager(players, stack, bus, triggers);
        var driver = new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent>
            {
                [alice] = new DeterministicBotAgent(),
                [bob] = new DeterministicBotAgent(),
            },
            stack, zones, triggers, resolver, sba, priorityMgr, combat,
            continuous);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        // CR 514.2 — after cleanup, control reverts and the haste grant ends.
        bear.Controller.Should().BeSameAs(bob, "control reverts to the owner at cleanup (CR 514.2)");
        CombatAbilities.HasHaste(bear).Should().BeFalse("the until-EOT haste grant ends at cleanup");
    }

    [Fact]
    public void TargetLeavesPlay_PruneFiresRevert_NoThrow()
    {
        var continuous = new ContinuousEffectsService();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = bob, Controller = bob, Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        bob.Zones.Battlefield.AddCard(bear);

        continuous.Register(new TemporaryControlChangeEffect(bear, alice));
        bear.Controller.Should().BeSameAs(alice);

        // Creature dies before cleanup → effect goes inactive → Prune drops it.
        bear.SetZone(ZoneType.Graveyard);
        continuous.Prune();

        // Reverting control on a permanent that has left the battlefield is a
        // harmless no-op; the test asserts the prune path does not throw.
        continuous.ExpireEndOfTurn();
    }
}
