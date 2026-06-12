using FluentAssertions;
using Majik.Bot.Combat;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Effect-free (vanilla) combat puzzle suite for Phase 1 of the MCTS bot search.
/// Each puzzle has a single unambiguous correct answer and tests that the search
/// finds it. Attacker puzzles (P1, P2) use <see cref="SearchStrategy.PickAttackers"/>;
/// blocker puzzles (P3–P5) use <see cref="SearchStrategy.PickBlockers"/>, which
/// (root block search) runs MCTS rooted at the defender's block decision against
/// the REAL declared attack via the engine's combat-state resume, with
/// <see cref="BlockCombatEval"/> as the fallback floor.
///
/// <para>
/// Architecture note: both decision classes run a real MCTS search over a
/// sandbox engine. The sandbox opponent (Task D3) is a
/// <see cref="BotPlayerAgent"/> backed by <see cref="Heuristic.HeuristicStrategy"/>
/// with a capped combat budget, so the sandbox IS adversarial: the opponent
/// declares blockers, allowing MCTS to observe trade outcomes and penalise
/// bad attacks (see P2). Block-puzzle boards must be FAITHFUL to the live
/// block prompt — the declared attacker is tapped (CR 508.1f) — because the
/// block search simulates the following turns, where tapped state matters.
/// </para>
/// </summary>
public class CombatPuzzleTests
{
    // ── Board-builder helpers ─────────────────────────────────────────────────

    private static Creature AddReadyCreature(Player owner, string name, int power, int toughness)
    {
        var c = new Creature(name, manaCost: string.Empty, power: power, toughness: toughness);
        c.ChangeOwner(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.ClearSummoningSickness();
        return c;
    }

    private static void PadLibraries(Player a, Player b, int count = 15)
    {
        for (int i = 0; i < count; i++)
        {
            var fa = new Land("Forest");
            fa.ChangeOwner(a);
            a.Zones.GetZone(ZoneType.Library).AddCard(fa);

            var fb = new Land("Forest");
            fb.ChangeOwner(b);
            b.Zones.GetZone(ZoneType.Library).AddCard(fb);
        }
    }

    private static SearchStrategy MctsStrategy() =>
        new(new BotConfig("Burn", Strategy: "mcts"));

    // ── Puzzle 1: Lethal swing ────────────────────────────────────────────────

    /// <summary>
    /// Puzzle 1 — Lethal swing.
    ///
    /// <para>
    /// Bot has two ready 2/2 creatures (total power 4). Opponent is at 3 life
    /// with no blockers. Swinging with both deals 4 damage — lethal. The search
    /// must choose to attack with all eligible attackers rather than pass.
    /// </para>
    ///
    /// Correct answer: attack with both 2/2 creatures (all-out attack).
    /// </summary>
    [Fact]
    public void P1_LethalSwing_AttacksWithAllCreatures()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 3); // 3 life — two 2/2 attackers are lethal

        var bearA = AddReadyCreature(bot, "BearA", 2, 2);
        var bearB = AddReadyCreature(bot, "BearB", 2, 2);
        PadLibraries(bot, opp);

        var ctx = SearchTestCtx.AtCombat(bot, opp);
        var strat = MctsStrategy();

        var plan = strat.PickAttackers(ctx, bot, new[] { bearA, bearB });

        // Must attack with both creatures; unambiguous lethal line.
        plan.Attackers.Should().HaveCount(2,
            because: "swinging for lethal with both 2/2s is the only winning line");

        // Returned references must be the LIVE creature objects (InstanceId remap).
        plan.Attackers.Select(a => a.Attacker.InstanceId)
            .Should().BeEquivalentTo(new[] { bearA.InstanceId, bearB.InstanceId },
                because: "PickAttackers must remap sandbox clones back to live objects");
    }

    // ── Puzzle 2: Don't attack into a bad trade ───────────────────────────────

    /// <summary>
    /// Puzzle 2 — Don't attack into a bad trade (verified via MCTS, Task D3).
    ///
    /// <para>
    /// Bot has a lone 2/2; opponent has an untapped 3/3 on the battlefield and
    /// sits at 20 life. Attacking is strictly bad: the 3/3 blocks and kills the
    /// 2/2 while surviving, and the bot gains nothing (opponent at 20 life loses
    /// nothing meaningful from 2 unblocked damage even if the 3/3 didn't block).
    /// </para>
    ///
    /// <para>
    /// Task D3 fix — adversarial sandbox opponent: the MCTS simulator now places
    /// a <see cref="BotPlayerAgent"/> backed by <see cref="Heuristic.HeuristicStrategy"/>
    /// on the opponent seat. When the bot declares the 2/2 as an attacker, the
    /// sandbox opponent declares the 3/3 as a blocker (hard-block: toughness 3 &gt;
    /// power 2). The MCTS sees the board after combat: bot lost its 2/2, opponent
    /// kept its 3/3, opponent life unchanged. BoardEval heavily penalises the
    /// board-power loss, so the hold-back line scores higher and MCTS chooses it.
    /// </para>
    ///
    /// Correct answer: do NOT attack (empty plan) — verified via MCTS search path.
    /// </summary>
    [Fact]
    public void P2_DontAttackIntoBadTrade_HoldsBack()
    {
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var bot22 = AddReadyCreature(bot, "Bot22", 2, 2);
        // Opponent's 3/3 is untapped and will block and survive, killing the bot's 2/2.
        AddReadyCreature(opp, "Opp33", 3, 3);

        PadLibraries(bot, opp);

        // Use MCTS (adversarial sandbox opponent) — Task D3 makes this work.
        // "Burn" weights are aggressive, so the fact it still holds back proves
        // the search correctly observes the trade via the blocking sandbox opponent.
        var ctx = SearchTestCtx.AtCombat(bot, opp);
        var strat = MctsStrategy();

        var plan = strat.PickAttackers(ctx, bot, new[] { bot22 });

        // MCTS sees that the sandbox opponent's 3/3 blocks and kills the 2/2 —
        // net board loss for the bot with no life-total gain against a 20-life
        // opponent. The hold-back line (empty attack) scores higher.
        plan.Attackers.Should().BeEmpty(
            because: "attacking the lone 2/2 into an untapped 3/3 at 20 life is a losing trade — " +
                     "the adversarial sandbox opponent blocks, so MCTS correctly holds back");
    }

    // ── Puzzle 3: Profitable block ────────────────────────────────────────────

    /// <summary>
    /// Puzzle 3 — Profitable block.
    ///
    /// <para>
    /// Opponent attacks with a 2/2. Bot has a 2/3. Blocking is strictly correct:
    /// the 2/3's toughness (3) exceeds the attacker's power (2) so it survives,
    /// while its power (2) equals the attacker's toughness (2) so the attacker
    /// dies. Net outcome: bot kills a 2/2 for free while keeping its 2/3 alive.
    /// </para>
    ///
    /// Correct answer: block with the 2/3 on the attacking 2/2.
    /// </summary>
    [Fact]
    public void P3_ProfitableBlock_BlocksWithSurvivingCreature()
    {
        var opp = new Player("Opp", 20); // attacker's turn
        var bot = new Player("Bot", 20);

        // Opponent attacks with a 2/2.
        var attacker22 = AddReadyCreature(opp, "Attacker22", 2, 2);

        // Bot has a 2/3 that can block profitably (survives, kills attacker).
        var blocker23 = AddReadyCreature(bot, "Blocker23", 2, 3);

        PadLibraries(bot, opp);

        var ctx = BlockSearchTestCtx.AtBlock(defender: bot, attacker: opp);
        var strat = MctsStrategy();

        var plan = strat.PickBlockers(ctx, bot,
            attackers: new[] { attacker22 },
            eligible: new[] { blocker23 });

        // The 2/3 must block the 2/2: kills the attacker and survives.
        plan.Blockers.Should().Contain(
            d => d.Blocker.InstanceId == blocker23.InstanceId
              && d.Attacker.InstanceId == attacker22.InstanceId,
            because: "a 2/3 blocking a 2/2 kills the attacker and survives — strictly profitable");
    }

    // ── Puzzle 4: Chump block to survive lethal ───────────────────────────────

    /// <summary>
    /// Puzzle 4 — Chump block to survive lethal.
    ///
    /// <para>
    /// Bot is at 4 life. Opponent attacks with a 5/5. If unblocked, 5 damage
    /// kills the bot (5 ≥ 4). Bot's only creature is a 1/1 that cannot survive
    /// (1 toughness ≤ 5 power), but chump-blocking reduces incoming unblocked
    /// damage to 0 and saves the bot's life.
    /// </para>
    ///
    /// <para>
    /// This exercises the lethal-aware heuristic in <see cref="BlockCombatEval"/>:
    /// no-block scores <c>double.MinValue</c> (the defender dies), so the chump
    /// block wins even though the blocker is sacrificed.
    /// </para>
    ///
    /// Correct answer: chump-block with the 1/1 to survive.
    /// </summary>
    [Fact]
    public void P4_ChumpToSurviveLethal_BlocksToPreventDeath()
    {
        var opp = new Player("Opp", 20);
        var bot = new Player("Bot", 4); // 4 life — 5/5 attack is lethal

        var bigAtt = AddReadyCreature(opp, "HillGiant55", 5, 5);
        var chump11 = AddReadyCreature(bot, "Chump11", 1, 1);

        PadLibraries(bot, opp);

        var ctx = BlockSearchTestCtx.AtBlock(defender: bot, attacker: opp);
        var strat = MctsStrategy();

        var plan = strat.PickBlockers(ctx, bot,
            attackers: new[] { bigAtt },
            eligible: new[] { chump11 });

        // The 1/1 must block the 5/5 even though it dies — surviving is all that matters.
        plan.Blockers.Should().Contain(
            d => d.Blocker.InstanceId == chump11.InstanceId,
            because: "chumping the 5/5 is the only way the bot (at 4 life) survives — " +
                     "lethal-aware scoring must prefer this over taking lethal damage");
    }

    // ── Puzzle 5: Don't chump when safe ──────────────────────────────────────

    /// <summary>
    /// Puzzle 5 — Don't chump when safe.
    ///
    /// <para>
    /// Bot is at 20 life. Opponent attacks with a 2/2. Bot has a 1/1. Taking
    /// 2 damage (down to 18 life) is entirely safe, and throwing away the 1/1
    /// (which dies to the 2/2 with no board gain) is a clear mistake.
    /// </para>
    ///
    /// <para>
    /// This guards <see cref="BlockCombatEval"/> against always-chump pathology:
    /// the near-lethal scaling (<c>threatScale = clamp(power / life, 0, 5)</c>)
    /// reduces the life-save weight when the threat is small relative to the
    /// defender's current life, so the board-cost of losing the 1/1 dominates
    /// and no-block is preferred.
    /// </para>
    ///
    /// Correct answer: do NOT block (empty plan / preserve the 1/1).
    /// </summary>
    [Fact]
    public void P5_DontChumpWhenSafe_PreservesCreature()
    {
        var opp = new Player("Opp", 20);
        var bot = new Player("Bot", 20); // safe at 20 life — 2 damage is irrelevant

        var attacker22 = AddReadyCreature(opp, "Bear22", 2, 2);
        // Board fidelity at the block prompt: a declared attacker is TAPPED
        // (CR 508.1f — declaring taps it unless vigilance). The root block
        // search simulates the FOLLOWING turns, where an untapped "attacker"
        // would unrealistically be free to block the bot's counterattack —
        // making the chump look better than it is.
        attacker22.Tap();
        var chump11 = AddReadyCreature(bot, "Weenie11", 1, 1);

        PadLibraries(bot, opp);

        var ctx = BlockSearchTestCtx.AtBlock(defender: bot, attacker: opp);
        var strat = MctsStrategy();

        var plan = strat.PickBlockers(ctx, bot,
            attackers: new[] { attacker22 },
            eligible: new[] { chump11 });

        // At 20 life, taking 2 damage is trivially safe. Throwing away the 1/1
        // (which dies to the 2/2) is strictly wrong — no-block must be preferred.
        plan.Blockers.Should().BeEmpty(
            because: "bot at 20 life has no reason to sacrifice its only 1/1 to stop " +
                     "2 damage from a 2/2 — no-block is clearly better");
    }
}
