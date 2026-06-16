using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Tests.TargetingPipeline;

/// <summary>
/// Shared minimal game world for the central-targeting tests: two players,
/// one creature + one planeswalker on the caster's battlefield, and one
/// instant spell on the stack. Built from real engine objects (no mocks) so
/// <see cref="Majik.Core.Targeting.TargetCandidateService.GatherCandidates"/>
/// enumerates exactly the zones it reads in production.
/// </summary>
internal static class TargetingTestWorld
{
    public static (GameContext ctx, Player caster) Build()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };
        alice.Zones.Battlefield.AddCard(bear);

        var walker = new Planeswalker("Test Walker", "3W", 4)
        {
            Owner = alice, Controller = alice, Zone = ZoneType.Battlefield,
        };
        alice.Zones.Battlefield.AddCard(walker);

        var stack = new Majik.Core.Stack.Stack();
        var bolt = new Instant("Lightning Bolt", "R") { Owner = bob, Zone = ZoneType.Stack };
        var spell = new Majik.Core.Spells.Spell(bolt, bob);
        stack.Push(spell);

        var ctx = new GameContext(
            alice, new[] { alice, bob }, alice, 1, StepStateType.PreCombatMain, stack);
        return (ctx, alice);
    }
}
