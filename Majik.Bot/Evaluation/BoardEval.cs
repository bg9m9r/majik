using Majik.Bot.Search;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Bot.Evaluation;

/// <summary>
/// Global state evaluator. Returns one scalar — higher = better for `self`.
/// Pure: reads only from `ctx` and `self`. Used by every non-combat decision
/// and as the leaf scorer alongside CombatEval.
/// </summary>
public static class BoardEval
{
    /// <summary>
    /// Starting life total used as the reference point for the lethal-proximity
    /// term. Matches the standard 20-life rule (Rule 103.3).
    /// </summary>
    private const int StartingLife = 20;

    /// <summary>
    /// Life-total threshold below which the marginal value of each additional
    /// damage point ramps sharply. At or below this value the quadratic bonus
    /// component kicks in, creating a steep gradient toward lethal.
    /// </summary>
    private const int LowLifeThreshold = 5;

    public static double Score(GameContext ctx, Player self, ArchetypeWeights weights)
    {
        var opp = FindOpponent(ctx, self);

        var lifeDelta = self.LifeTotal - opp.LifeTotal;
        var boardPower = SumPower(self);
        var boardToughness = SumToughness(self);
        var oppThreats = SumPower(opp);
        var manaSources = CountLands(self);
        var handSize = self.Zones.Hand.Count;
        var tempo = ctx.ActivePlayer == opp ? CountUntappedLands(self) : 0;
        var keyCard = HasKeyCardInPlay(self) ? 1 : 0;
        var lethalProx = LethalProximityBonus(opp.LifeTotal);
        // Card-advantage differential: being up cards on the opponent is the
        // core attrition signal for control/midrange. Positive = ahead on cards.
        var cardAdvDiff = self.Zones.Hand.Count - opp.Zones.Hand.Count;
        // Planeswalker-engine bonus: each planeswalker the bot controls
        // contributes its current loyalty as a proxy for accumulated/future
        // value. A Teferi at 7 loyalty has already generated card advantage and
        // threatens to keep doing so each turn (see ArchetypeWeights.PlaneswalkerEngine).
        var planeswalkerEngine = SumPlaneswalkerLoyalty(self);
        // Hidden-burn-reach penalty: dangerous sampled worlds eval dangerous.
        // Subtracted (positive weight REDUCES the searched seat's score).
        var hiddenReach = HiddenReachPenalty(self, opp);

        return
              weights.LifeDelta           * lifeDelta
            + weights.BoardPower          * boardPower
            + weights.BoardToughness      * boardToughness
            + weights.OpponentThreats     * oppThreats
            + weights.ManaSources         * manaSources
            + weights.HandSize            * handSize
            + weights.Tempo               * tempo
            + weights.KeyCardInPlay       * keyCard
            + weights.LethalProximity     * lethalProx
            + weights.CardAdvantage       * cardAdvDiff
            + weights.PlaneswalkerEngine  * planeswalkerEngine
            - weights.HiddenReach         * hiddenReach;
    }

    /// <summary>
    /// Penalty for being within burn reach of the opponent's (sandbox) hand. In determinized
    /// worlds that hand is SAMPLED — the bot's own honest guess — so reading it leaks nothing;
    /// in the perfect-info path it reads the real hand (that path already peeks wholesale —
    /// explicit design decision: the term is active in ALL eval paths for consistency).
    /// reach = Σ direct damage of opponent hand spells castable soon (mana value &lt;= opp lands + 2);
    /// penalty = max(0, reach - (selfLife - margin)), margin 1.
    /// </summary>
    internal static double HiddenReachPenalty(Player self, Player opp)
    {
        var lands = CountLands(opp);
        var reach = 0;
        foreach (var card in opp.Zones.Hand.GetCards())
        {
            var dmg = DirectDamageRecognizer.DamageToPlayer(card);
            if (dmg <= 0) continue;
            if (ManaValueOf(card) <= lands + 2) reach += dmg;
        }
        const int Margin = 1;
        return Math.Max(0, reach - (self.LifeTotal - Margin));
    }

    /// <summary>Mana value of a card via its parsed mana cost (no ICard.Cmc exists).</summary>
    private static int ManaValueOf(ICard card)
        => ManaCost.Parse(card.ManaCost ?? string.Empty).TotalValue;

    /// <summary>
    /// Non-linear bonus for driving the opponent's life total toward zero.
    /// Returns a value that grows as <paramref name="oppLife"/> shrinks, with
    /// a steep quadratic ramp when opp is within <see cref="LowLifeThreshold"/>
    /// life of death. Terminal (opp at 0) dominates all other terms because the
    /// engine ends the game before eval runs — this term is a gradient, not a
    /// win condition.
    ///
    /// <para>
    /// Formula: linear base (StartingLife - oppLife) + quadratic ramp when
    /// oppLife &lt;= LowLifeThreshold. The ramp adds
    /// <c>(LowLifeThreshold - oppLife)^2</c> extra points so each point of
    /// damage when the opponent is at 3 life is worth significantly more than
    /// the same point at 15 life. This makes both bots race to finish games
    /// rather than stalling at a partial board advantage.
    /// </para>
    ///
    /// <para>
    /// Examples (opp life → bonus before weight multiplier):
    /// <list type="bullet">
    ///   <item>20 → 0 (baseline, no reward yet)</item>
    ///   <item>15 → 5 (linear only)</item>
    ///   <item>10 → 10 (linear only)</item>
    ///   <item>5 → 15 (linear, ramp starts here at +0)</item>
    ///   <item>3 → 17 + 4 = 21 (linear + (5-3)^2 = 4)</item>
    ///   <item>1 → 19 + 16 = 35 (linear + (5-1)^2 = 16)</item>
    ///   <item>0 → 20 + 25 = 45 (engine ends game before reaching; dominated by
    ///     terminal win/loss)</item>
    /// </list>
    /// </para>
    /// </summary>
    public static double LethalProximityBonus(int oppLife)
    {
        // Clamp to avoid negative values when opp has gained life above starting.
        var linear = Math.Max(0, StartingLife - oppLife);
        if (oppLife >= LowLifeThreshold)
            return linear;
        // Quadratic ramp: steep bonus per life-point in the danger zone.
        var ramp = (double)(LowLifeThreshold - oppLife) * (LowLifeThreshold - oppLife);
        return linear + ramp;
    }

    private static Player FindOpponent(GameContext ctx, Player self)
        => ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, self))
           ?? throw new InvalidOperationException("BoardEval: no opponent found in ctx.AllPlayers");

    private static int SumPower(Player p)
        => p.Zones.Battlefield.GetCards().OfType<Creature>().Sum(c => Math.Max(0, c.Power));

    private static int SumToughness(Player p)
        => p.Zones.Battlefield.GetCards().OfType<Creature>().Sum(c => Math.Max(0, c.Toughness));

    private static int CountLands(Player p)
        => p.Zones.Battlefield.GetCards().Count(c => c is Land);

    private static int CountUntappedLands(Player p)
        => p.Zones.Battlefield.GetCards().OfType<Land>().Count(l => !IsTapped(l));

    private static bool IsTapped(ICard c)
        => c is Permanent perm && perm.IsTapped;

    private static bool HasKeyCardInPlay(Player p)
    {
        return p.Zones.Battlefield.GetCards().OfType<Creature>().Any(c => c.Power >= 4);
    }

    /// <summary>
    /// Sum of current loyalty across all planeswalkers the player controls.
    /// Loyalty is a proxy for accumulated value: each activation has already
    /// produced an effect and raised/lowered loyalty, so high loyalty means
    /// the walker has been ticking up (card draws, bounces) while staying
    /// alive. Used by the <see cref="ArchetypeWeights.PlaneswalkerEngine"/> term.
    ///
    /// <para>Uses <see cref="Permanent.IsEffectivePlaneswalker"/> so DFC
    /// backs (creature front / planeswalker back) are counted even though their
    /// C# type is <see cref="Permanent"/> rather than
    /// <see cref="Planeswalker"/>.</para>
    /// </summary>
    private static int SumPlaneswalkerLoyalty(Player p)
        => p.Zones.Battlefield.GetCards()
               .OfType<Permanent>()
               .Where(perm => perm.IsEffectivePlaneswalker())
               .Sum(perm => perm.GetEffectiveLoyalty() ?? 0);
}
