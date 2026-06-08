using FluentAssertions;
using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Focused tests for Task D3: the MCTS sandbox opponent is adversarial.
///
/// <para>
/// Since Task D3, <see cref="EngineSimulator"/> places a
/// <see cref="BotPlayerAgent"/> backed by <see cref="Heuristic.HeuristicStrategy"/>
/// on the opponent seat (with a capped 20 ms combat budget). When the searched
/// bot declares attackers the sandbox opponent now declares blockers, so MCTS can
/// observe trades and correctly penalise bad attacks.
/// </para>
/// </summary>
public class AdversarialOpponentTests
{
    // ── Board builder helpers ─────────────────────────────────────────────────

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

    // ── Test ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bot has a lone 2/2 (ready). Opponent has an untapped 3/3. No bot life
    /// pressure (both at 20 life), no upside to attacking.
    ///
    /// <para>
    /// With the adversarial sandbox opponent (HeuristicStrategy), the sandbox
    /// opponent's greedy hard-block logic assigns the 3/3 to block the 2/2
    /// (3/3 toughness 3 &gt; 2/2 power 2 — always a hard-block). After combat:
    /// bot's 2/2 dies, opponent's 3/3 survives, opponent's life total unchanged.
    /// <see cref="Evaluation.BoardEval"/> scores this as a board-power loss for
    /// the bot, so the hold-back line (no attack) scores higher across all MCTS
    /// rollouts and the search returns an empty plan.
    /// </para>
    /// </summary>
    [Fact]
    public void AttackSearch_AvoidsBadTrade_BecauseOpponentBlocks()
    {
        // Bot: lone 2/2 (ready). Opponent: untapped 3/3. Both at 20 life.
        // No life pressure; no upside to attacking into the 3/3.
        var bot = new Player("Bot", 20);
        var opp = new Player("Opp", 20);

        var twoTwo = AddReadyCreature(bot, "BotBear22", 2, 2);
        AddReadyCreature(opp, "OppOgre33", 3, 3);

        // Pad libraries so the engine does not draw-lose immediately.
        PadLibraries(bot, opp);

        var ctx = SearchTestCtx.AtCombat(bot, opp);

        // SearchStrategy with MCTS — the adversarial opponent blocks in every sandbox.
        var strat = new SearchStrategy(new BotConfig("Burn", Strategy: "mcts"));

        var plan = strat.PickAttackers(ctx, bot, new[] { twoTwo });

        // The 3/3 hard-blocks the 2/2; MCTS sees the board-power loss and holds back.
        plan.Attackers.Should().BeEmpty(
            because: "the adversarial sandbox opponent's 3/3 blocks the 2/2, destroying it " +
                     "for no life-total gain — MCTS must recognise the trade as losing and not attack");
    }
}
