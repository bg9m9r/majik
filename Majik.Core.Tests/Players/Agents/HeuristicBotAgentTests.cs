using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class HeuristicBotAgentTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public async Task Priority_WithLandInHand_OwnMainPhase_PlaysLand()
    {
        var land = NamedCardFactory.Create("Mountain", _alice);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);

        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.Main, new Majik.Core.Stack.Stack());

        var action = await bot.ChoosePriorityActionAsync(ctx);

        action.Should().BeOfType<PriorityAction.PlayLand>()
            .Which.Land.Should().BeSameAs(land);
    }

    [Fact]
    public async Task Priority_NoLand_Passes()
    {
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.Main, new Majik.Core.Stack.Stack());

        (await bot.ChoosePriorityActionAsync(ctx)).Should().Be(PriorityAction.Pass);
    }

    [Fact]
    public async Task Priority_OpponentTurn_Passes_EvenWithLand()
    {
        var land = NamedCardFactory.Create("Mountain", _alice);
        land.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(land);

        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.Main, new Majik.Core.Stack.Stack());

        (await bot.ChoosePriorityActionAsync(ctx)).Should().Be(PriorityAction.Pass);
    }

    [Fact]
    public async Task DeclareAttackers_SwingsWithEveryEligibleCreature()
    {
        var b1 = new Creature("B1", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        var b2 = new Creature("B2", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice,
            1, PhaseStateType.DeclareAttackers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareAttackersAsync(ctx, new[] { b1, b2 });

        plan.Attackers.Should().HaveCount(2);
        plan.Attackers.Select(a => a.DefendingPlayerOrPlaneswalker).Should().AllBeEquivalentTo(_bob);
    }

    [Fact]
    public async Task DeclareBlockers_PrefersSafeAndKillsAttacker()
    {
        // Attacker 2/2. Blockers: 1/1 (suicide), 1/3 (safe but doesn't kill),
        // 4/4 (safe AND kills attacker). Smart heuristic prefers the
        // one-sided kill — 4/4.
        var attacker = new Creature("Atk", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var small = new Creature("Small", "G", 1, 1) { Owner = _alice, Controller = _alice };
        var safeNoKill = new Creature("SafeNoKill", "1G", 1, 3) { Owner = _alice, Controller = _alice };
        var bigger = new Creature("Big", "2G", 4, 4) { Owner = _alice, Controller = _alice };
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.DeclareBlockers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareBlockersAsync(ctx, new[] { attacker }, new[] { small, safeNoKill, bigger });

        plan.Blockers.Should().HaveCount(1);
        plan.Blockers[0].Blocker.Should().BeSameAs(bigger);
    }

    [Fact]
    public async Task DeclareBlockers_NoSafeKill_FallsBackToSafeBlock()
    {
        // Attacker 2/2. Only safe blocker is 1/3 (survives, doesn't kill).
        var attacker = new Creature("Atk", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var safeNoKill = new Creature("SafeNoKill", "1G", 1, 3) { Owner = _alice, Controller = _alice };
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.DeclareBlockers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareBlockersAsync(ctx, new[] { attacker }, new[] { safeNoKill });

        plan.Blockers.Should().HaveCount(1);
        plan.Blockers[0].Blocker.Should().BeSameAs(safeNoKill);
    }

    [Fact]
    public async Task DeclareBlockers_ChumpsWhenLethal()
    {
        // 20 incoming damage vs Alice at 5 life — must chump even though
        // bear dies for nothing.
        var huge = new Creature("Huge", "5G", 20, 20) { Owner = _bob, Controller = _bob };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        _alice.LifeTotal = 5;
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.DeclareBlockers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareBlockersAsync(ctx, new[] { huge }, new[] { bear });

        plan.Blockers.Should().HaveCount(1);
        plan.Blockers[0].Blocker.Should().BeSameAs(bear);
    }

    [Fact]
    public async Task DeclareBlockers_NoSafeBlocker_DoesNotBlock()
    {
        var huge = new Creature("Huge", "5G", 10, 10) { Owner = _bob, Controller = _bob };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.DeclareBlockers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareBlockersAsync(ctx, new[] { huge }, new[] { bear });

        plan.Blockers.Should().BeEmpty();
    }
}
