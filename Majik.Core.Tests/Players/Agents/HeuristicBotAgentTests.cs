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

    private static Creature WithKeyword(Creature c, string keyword)
    {
        c.AddAbility(new Majik.Core.Abilities.KeywordAbility(keyword, c, c.Controller!));
        return c;
    }

    [Fact]
    public async Task DeclareBlockers_DeathtoucherKillsBigAttacker()
    {
        // 1/1 deathtouch can profitably block a 5/5 — sacrifice 1 CMC to
        // kill a 4 CMC creature. Bot prefers it over a 4/4 safe-kill.
        var atk = new Creature("Big", "3GG", 5, 5) { Owner = _bob, Controller = _bob };
        var dt = WithKeyword(new Creature("DT", "B", 1, 1) { Owner = _alice, Controller = _alice }, "Deathtouch");
        var safe = new Creature("Wall", "3", 4, 5) { Owner = _alice, Controller = _alice };
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.DeclareBlockers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareBlockersAsync(ctx, new[] { atk }, new[] { dt, safe });

        plan.Blockers.Should().HaveCount(1);
        plan.Blockers[0].Blocker.Should().BeSameAs(dt);
    }

    [Fact]
    public async Task DeclareBlockers_FirstStrikeAttacker_BlockerDoesNotTrade()
    {
        // Attacker first strike 2/2; blocker no FS 2/2. Attacker kills
        // blocker BEFORE blocker damages — not a trade. Bot should NOT
        // block (no benefit) unless lethal.
        var atk = WithKeyword(new Creature("FS", "1W", 2, 2) { Owner = _bob, Controller = _bob }, "First strike");
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice };
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.DeclareBlockers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareBlockersAsync(ctx, new[] { atk }, new[] { bear });

        plan.Blockers.Should().BeEmpty("first strike means bear dies before dealing damage — no trade");
    }

    [Fact]
    public async Task DeclareBlockers_Menace_GangBlocksWithTwo()
    {
        // Menace 3/3 — requires 2+ blockers. Two 2/2s gang up.
        var atk = WithKeyword(new Creature("MenaceCreature", "2R", 3, 3) { Owner = _bob, Controller = _bob }, "Menace");
        var b1 = new Creature("B1", "1W", 2, 2) { Owner = _alice, Controller = _alice };
        var b2 = new Creature("B2", "1W", 2, 2) { Owner = _alice, Controller = _alice };
        var bot = new HeuristicBotAgent();
        _alice.LifeTotal = 5; // make lethal so the gang is justified
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.DeclareBlockers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareBlockersAsync(ctx, new[] { atk }, new[] { b1, b2 });

        plan.Blockers.Should().HaveCount(2);
        plan.Blockers.Select(b => b.Attacker).Should().AllBeEquivalentTo(atk);
    }

    [Fact]
    public async Task DeclareBlockers_SortsByThreatScore_BigGetsBestBlocker()
    {
        // Two attackers: 5/5 tramplelifelink vs 2/2 vanilla. Best blocker
        // is a 5/5 safe-kill — should go on the 5/5, NOT the 2/2.
        var bigAtk = WithKeyword(WithKeyword(
            new Creature("Big", "3GG", 5, 5) { Owner = _bob, Controller = _bob },
            "Trample"), "Lifelink");
        var smallAtk = new Creature("Small", "1G", 2, 2) { Owner = _bob, Controller = _bob };
        var bigBlocker = new Creature("BigBlocker", "3WW", 5, 5) { Owner = _alice, Controller = _alice };
        var smallBlocker = new Creature("SmallBlocker", "1W", 2, 2) { Owner = _alice, Controller = _alice };
        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob,
            1, PhaseStateType.DeclareBlockers, new Majik.Core.Stack.Stack());

        var plan = await bot.DeclareBlockersAsync(ctx, new[] { bigAtk, smallAtk }, new[] { bigBlocker, smallBlocker });

        plan.Blockers.Should().Contain(b => b.Blocker == bigBlocker && b.Attacker == bigAtk);
    }
}
