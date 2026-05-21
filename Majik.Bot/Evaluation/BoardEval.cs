using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Bot.Evaluation;

/// <summary>
/// Global state evaluator. Returns one scalar — higher = better for `self`.
/// Pure: reads only from `ctx` and `self`. Used by every non-combat decision
/// and as the leaf scorer alongside CombatEval.
/// </summary>
public static class BoardEval
{
    public static double Score(GameContext ctx, Player self, ArchetypeWeights weights)
    {
        var opp = FindOpponent(ctx, self);

        var lifeDelta = self.LifeTotal - opp.LifeTotal;
        var boardPower = SumPower(self);
        var boardToughness = SumToughness(self);
        var oppThreats = SumPower(opp);
        var manaSources = CountLandsAndRocks(self);
        var handSize = self.Zones.Hand.Count;
        var tempo = ctx.ActivePlayer == opp ? CountUntappedLands(self) : 0;
        var keyCard = HasKeyCardInPlay(self) ? 1 : 0;

        return
              weights.LifeDelta        * lifeDelta
            + weights.BoardPower       * boardPower
            + weights.BoardToughness   * boardToughness
            + weights.OpponentThreats  * oppThreats
            + weights.ManaSources      * manaSources
            + weights.HandSize         * handSize
            + weights.Tempo            * tempo
            + weights.KeyCardInPlay    * keyCard;
    }

    private static Player FindOpponent(GameContext ctx, Player self)
        => ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, self))
           ?? throw new InvalidOperationException("BoardEval: no opponent found in ctx.AllPlayers");

    private static int SumPower(Player p)
        => p.Zones.Battlefield.GetCards().OfType<Creature>().Sum(c => Math.Max(0, c.Power));

    private static int SumToughness(Player p)
        => p.Zones.Battlefield.GetCards().OfType<Creature>().Sum(c => Math.Max(0, c.Toughness));

    private static int CountLandsAndRocks(Player p)
        => p.Zones.Battlefield.GetCards().Count(c => c is Land);

    private static int CountUntappedLands(Player p)
        => p.Zones.Battlefield.GetCards().OfType<Land>().Count(l => !IsTapped(l));

    private static bool IsTapped(ICard c)
        => c is Permanent perm && perm.IsTapped;

    private static bool HasKeyCardInPlay(Player p)
    {
        return p.Zones.Battlefield.GetCards().OfType<Creature>().Any(c => c.Power >= 4);
    }
}
