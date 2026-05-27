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
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Integration;

/// <summary>
/// Real game slice end-to-end:
///   - Alice has Mountain (untapped) and Lightning Bolt in hand
///   - Alice casts Bolt at Bob via SpellCastFlow + ManaPaymentResolver
///   - Spell resolves via StackResolver, Bob loses 3 life
///   - Alice's Grizzly Bears attacks unblocked → Bob loses 2 more
/// </summary>
public class FirstCombatEndToEndTests
{
    [Fact]
    public async Task BoltThenAttack_BobAt15()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(stack, bus);
        var resolver = new StackResolver(bus, zones);
        var sba = new StateBasedActions(bus, zones, triggers);
        var castFlow = new SpellCastFlow(stack, zones, bus);
        var manaResolver = new ManaPaymentResolver();
        var combat = new CombatFlow(bus, sba);

        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Pre-game battlefield: Mountain (Alice), Grizzly Bears (Alice, no summoning sickness)
        var mountain = NamedCardFactory.Create("Mountain", alice);
        mountain.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(mountain);
        var bear = (Creature)NamedCardFactory.Create("Grizzly Bears", alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.HasSummoningSickness = false;
        alice.Zones.Battlefield.AddCard(bear);

        // Bolt in hand
        var bolt = NamedCardFactory.Create("Lightning Bolt", alice);
        bolt.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(bolt);

        // Cast flow: pay R from Mountain, target Bob.
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueTargets(new[] { (object)bob });
        aliceAgent.QueueMana(new ManaPayment(new[] { mountain }));

        // 1) Pay mana first (in real engine SpellCastFlow would call resolver; here we
        //    pre-pay so the flow can build with empty payment).
        manaResolver.Pay(alice, ManaCost.Parse("R"), new ManaPayment(new[] { mountain }))
            .Should().BeTrue();

        // 2) Cast Bolt — SpellCastFlow needs an agent for targets/mana.
        var manaSubAgent = new ScriptedAgent();
        manaSubAgent.QueueTargets(new[] { (object)bob });
        manaSubAgent.QueueMana(ManaPayment.Empty); // already paid externally
        var ctx = new GameContext(alice, new[] { alice, bob }, alice, 1, PhaseStateType.PreCombatMain, stack);

        var def = Majik.Core.CardData.OracleSpellBinder.Bind(
            new Majik.Core.CardData.CardEntity
            {
                Name = "Lightning Bolt",
                ManaCost = "{R}",
                OracleText = "Lightning Bolt deals 3 damage to any target.",
            },
            alice, raw => raw, stack: null)!;
        await castFlow.CastAsync(alice, bolt, def, manaSubAgent, ctx);

        // 3) Resolve top of stack → Bolt resolves → Bob loses 3.
        resolver.ResolveTop(stack);
        bob.LifeTotal.Should().Be(17);
        bolt.Zone.Should().Be(ZoneType.Graveyard);

        // 4) Combat: Alice attacks with Bear, Bob declines to block.
        var attackerAgent = new ScriptedAgent();
        attackerAgent.QueueAttackers(new CombatPlan(new[]
        {
            new Majik.Core.Players.Agents.AttackerDeclaration(bear, bob),
        }));
        var blockerAgent = new ScriptedAgent();
        blockerAgent.QueueBlockers(BlockPlan.None);

        var combatCtx = new GameContext(alice, new[] { alice, bob }, alice, 1, PhaseStateType.DeclareAttackers, stack);
        await combat.RunCombatAsync(
            attacker: alice, defender: bob,
            attackerAgent: attackerAgent, defenderAgent: blockerAgent,
            attackers: new[] { bear }, blockers: Array.Empty<Creature>(),
            ctx: combatCtx);

        bob.LifeTotal.Should().Be(15);
        bear.IsTapped.Should().BeTrue();
        alice.LifeTotal.Should().Be(20);
    }
}
