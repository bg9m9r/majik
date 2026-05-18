using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Integration;

/// <summary>
/// End-to-end: Soul-Warden analog. A creature with "When ~ enters the
/// battlefield, you gain 1 life" enters → trigger fires → enqueued → drained
/// at next priority → resolves → life increases.
/// </summary>
public class EtbTriggerEndToEndTests
{
    [Fact]
    public void SoulWarden_GainsLifeOnSelfEnter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new PriorityManager(
            new List<Player> { alice, bob }, stack, bus, triggers);
        var resolver = new StackResolver(bus, zones);

        var warden = new Creature("Soul Warden", "W", 1, 1) { Owner = alice, Zone = ZoneType.Hand };
        var etb = new TriggeredAbility(
            warden, alice,
            Triggers.OnEnterBattlefieldSelf(warden),
            effects: new IEffect[] { new Effect("gain 1 life", () => alice.GainLife(1)) });
        warden.AddAbility(etb);
        triggers.BindCard(warden);

        // Cast resolves → card moves to battlefield via ZoneService.
        zones.MoveCardTo(warden, ZoneType.Battlefield, controller: alice);

        // Trigger should have been queued by TriggerManager via SubscribeAll.
        triggers.PendingCount.Should().Be(1);
        alice.LifeTotal.Should().Be(20);

        // Next time a player would receive priority, drain occurs.
        priority.InitializeForPhase(alice);

        triggers.PendingCount.Should().Be(0);
        stack.Count.Should().Be(1);

        // Both players pass priority — resolver pops + resolves.
        resolver.ResolveTop(stack);

        alice.LifeTotal.Should().Be(21);
        stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void TwoSoulWardens_ApnapOrderedOntoStack()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var priority = new PriorityManager(
            new List<Player> { alice, bob }, stack, bus, triggers);

        // Both players control a Soul Warden analog that triggers on ANY
        // creature entering the battlefield.
        var aliceWarden = MakeAnyEtbLifeGainer("Alice Warden", alice);
        var bobWarden = MakeAnyEtbLifeGainer("Bob Warden", bob);
        aliceWarden.Zone = ZoneType.Battlefield;
        bobWarden.Zone = ZoneType.Battlefield;
        triggers.BindCard(aliceWarden);
        triggers.BindCard(bobWarden);

        // A third creature enters.
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Zone = ZoneType.Hand };
        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: alice);

        triggers.PendingCount.Should().Be(2);

        // Drain — active player (Alice) trigger goes on stack first → Bob's on top.
        priority.InitializeForPhase(alice);

        stack.Count.Should().Be(2);
        var top = stack.Top!;
        ((ITriggeredAbility)top).Controller.Should().BeSameAs(bob,
            because: "APNAP push order leaves the NAP's trigger on top, so it resolves first");
    }

    private static Creature MakeAnyEtbLifeGainer(string name, Player controller)
    {
        var card = new Creature(name, "W", 1, 1) { Owner = controller };
        var ability = new TriggeredAbility(
            card, controller,
            Triggers.OnAnyCreatureEntersBattlefield(),
            effects: new IEffect[] { new Effect("gain 1", () => controller.GainLife(1)) });
        card.AddAbility(ability);
        return card;
    }
}
