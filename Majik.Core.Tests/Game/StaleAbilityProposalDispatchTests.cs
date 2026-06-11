using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 602.2b / 601.2h-analogue — regression for the live-match crash
/// "InvalidPlayerActionException: Cannot pay cost: R".
///
/// The bot enumerates ActivateAbility proposals against POTENTIAL mana
/// (floating pool + untapped sources, colour-blind —
/// LegalActionEnumerator.CanAffordAbility), but TurnDriver's DispatchActivate
/// pays via AbilityActivator → CostPayment.PayCosts, which draws from the
/// FLOATING POOL only (ManaCostCost.CanPay → ManaPool.CanPay). A proposal
/// whose mana was never floated (or whose pool emptied between proposal and
/// execution) used to reach PayCosts unaffordably; the resulting
/// InvalidPlayerActionException is NOT an InvalidOperationException, so
/// DispatchActivate's swallow-catch missed it and the throw tore down the
/// whole game (the dominant heuristic-vs-heuristic crash in the strength
/// probes). The fix re-validates affordability AT DISPATCH (stale proposals
/// are swallowed like illegal PlayLand proposals) and catches the
/// validation-throw type as defensive depth.
/// </summary>
public class StaleAbilityProposalDispatchTests
{
    private static TurnDriver BuildDriver(
        Player alice, Player bob,
        IPlayerAgent aliceAgent, IPlayerAgent bobAgent,
        out Majik.Core.Stack.Stack stack)
    {
        var bus = new EventBus();
        stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var players = new List<Player> { alice, bob };
        var priority = new PriorityManager(players, stack, bus, triggers);

        // Seed minimal libraries so the draw step works.
        foreach (var p in players)
        {
            for (var i = 0; i < 5; i++)
            {
                var c = NamedCardFactory.Create("Mountain", p);
                p.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }

        return new TurnDriver(
            players,
            new Dictionary<Player, IPlayerAgent>
            { [alice] = aliceAgent, [bob] = bobAgent },
            stack, zones, triggers, resolver, sba, priority,
            new CombatFlow(bus, sba),
            continuousEffects: null,
            landDropTracker: new LandDropTracker());
    }

    /// <summary>
    /// The crash shape: the agent proposes activating a {R}-cost ability while
    /// its floating pool is EMPTY. The dispatch must swallow the stale
    /// proposal (no throw, no stack object, no cost paid) and the turn must
    /// complete normally.
    /// </summary>
    [Fact]
    public async Task ActivateProposal_WithEmptyManaPool_IsSwallowed_NotACrash()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var source = new Artifact("Staff of Fire", "") { Owner = alice, Controller = alice };
        source.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(source);

        var effectRan = false;
        var ability = new ActivatedAbility(
            source, alice,
            costs: new ICost[] { new ManaCostCost("R") },
            effects: new[] { Fx.Inline("1 damage to Bob", () => { effectRan = true; bob.LoseLife(1); }) });

        var aliceAgent = new ScriptedAgent();
        // First window: propose the unaffordable activation (pool is empty).
        aliceAgent.QueuePriority(new PriorityAction.ActivateAbility(ability, System.Array.Empty<object>()));
        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 200; i++)
        {
            aliceAgent.QueuePriority(PriorityAction.Pass);
            bobAgent.QueuePriority(PriorityAction.Pass);
        }

        var driver = BuildDriver(alice, bob, aliceAgent, bobAgent, out var stack);

        var act = async () => await driver.RunTurnAsync(alice, turnNumber: 2);

        await act.Should().NotThrowAsync(
            "an unaffordable (stale) ActivateAbility proposal must be swallowed at " +
            "dispatch, not crash the game with 'Cannot pay cost: R'");

        effectRan.Should().BeFalse("the unaffordable activation never happened");
        stack.IsEmpty.Should().BeTrue("nothing was put on the stack");
        bob.LifeTotal.Should().Be(20);
    }

    /// <summary>
    /// Control: with the mana already FLOATING the same proposal activates
    /// normally — the dispatch-time affordability gate only rejects
    /// genuinely unpayable proposals.
    /// </summary>
    [Fact]
    public async Task ActivateProposal_WithFloatingMana_StillActivates()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var source = new Artifact("Staff of Fire", "") { Owner = alice, Controller = alice };
        source.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(source);

        var effectRan = false;
        var ability = new ActivatedAbility(
            source, alice,
            costs: new ICost[] { new ManaCostCost("R") },
            effects: new[] { Fx.Inline("1 damage to Bob", () => { effectRan = true; bob.LoseLife(1); }) });

        alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("R"));

        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueuePriority(new PriorityAction.ActivateAbility(ability, System.Array.Empty<object>()));
        var bobAgent = new ScriptedAgent();
        for (var i = 0; i < 200; i++)
        {
            aliceAgent.QueuePriority(PriorityAction.Pass);
            bobAgent.QueuePriority(PriorityAction.Pass);
        }

        var driver = BuildDriver(alice, bob, aliceAgent, bobAgent, out _);

        await driver.RunTurnAsync(alice, turnNumber: 2);

        effectRan.Should().BeTrue("the affordable activation went on the stack and resolved");
        bob.LifeTotal.Should().Be(19);
        alice.ManaPool.Red.Should().Be(0, "the {R} was paid");
    }
}
