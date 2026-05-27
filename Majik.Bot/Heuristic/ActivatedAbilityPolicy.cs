using Majik.Bot.Evaluation;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Closed-form EV projection for activating a single
/// <see cref="IActivatedAbility"/>. Mirrors the
/// <see cref="PriorityPolicy.ProjectCastDelta"/> shape — pure
/// <see cref="BoardEval"/>-delta math, no engine mutation — so the
/// activated-ability candidate can be argmax'd alongside cast / play-land
/// candidates.
///
/// <para>The projection weighs two halves:</para>
/// <list type="bullet">
///   <item><b>Cost side</b> (always negative): pay-life burns LifeDelta;
///   sacrificing a creature loses BoardPower / Toughness; tapping the
///   source on a creature loses Tempo (it can't attack this turn). Mana
///   costs are NOT subtracted here — the priority loop already treats
///   spent mana as fungible inside a single window (the BoardEval
///   ManaSources term is "lands in play", not "untapped mana").</item>
///   <item><b>Effect side</b>: sniff the effect <see cref="IEffect.Description"/>
///   strings for intent verbs ("destroy", "damage", "draw", "+1/+1",
///   "create token", "gain N life", "counter target") and map each to a
///   BoardEval component delta scaled by the same archetype weights used
///   everywhere else. Without a hit, defaults to a small positive Tempo
///   bump so a free / mana-only activation still beats Pass when nothing
///   else is competing for the slot.</item>
/// </list>
///
/// <para><b>Phase awareness.</b> Pump-style activations (counters, "gets
/// +X/+X") only score their full BoardPower delta when combat is upcoming
/// or in progress — pumping a creature in our post-combat main is wasted
/// tempo. Removal / burn / draw don't care about phase.</para>
///
/// <para>The bot's outer pump-loop is responsible for "fired once per
/// turn" memo, not this scorer — projection is a pure function of state.</para>
/// </summary>
public static class ActivatedAbilityPolicy
{
    /// <summary>Projected BoardEval delta if this activation resolves
    /// successfully. May be negative (cost outweighs benefit) — the
    /// outer argmax will pass on negative-delta activations.</summary>
    public static double ProjectActivateDelta(
        IActivatedAbility ability,
        GameContext ctx,
        Player self,
        ArchetypeWeights weights)
    {
        var costDelta = CostDelta(ability, self, weights);
        var effectDelta = EffectDelta(ability, ctx, self, weights);
        return costDelta + effectDelta;
    }

    // ----- Cost side -----

    /// <summary>Negative delta from paying the activation's costs.
    /// Mana costs are excluded — the BoardEval ManaSources term counts
    /// lands-in-play (not untapped mana), so spending mana for an activation
    /// in our own main doesn't shrink the eval. Tap / sacrifice / pay-life
    /// DO shrink eval because they move state outside that window.</summary>
    private static double CostDelta(IActivatedAbility ability, Player self, ArchetypeWeights weights)
    {
        double delta = 0;
        foreach (var cost in ability.Costs)
        {
            switch (cost)
            {
                case AdditionalCost ac:
                    switch (ac.CostType)
                    {
                        case AdditionalCostType.PayLife:
                            // PayLife N: -LifeDelta * N. Description carries
                            // the number; cheapest parse is regex-free string
                            // scan since cost text is "Pay N life".
                            delta -= weights.LifeDelta * ExtractInt(ac.Description, fallback: 1);
                            break;
                        case AdditionalCostType.Sacrifice:
                            // Sacrificing a creature loses BoardPower + Toughness
                            // (best-case proxy: average vanilla 2/2). Sacrificing
                            // self when source is a creature loses its actual
                            // stats. Non-creature sacrifices lose a ManaSources
                            // unit (close enough for v1 — a sac'd Treasure /
                            // Clue is a one-shot tempo loss).
                            if (ability.Source is Creature selfCrt)
                            {
                                delta -= weights.BoardPower * Math.Max(0, selfCrt.Power);
                                delta -= weights.BoardToughness * Math.Max(0, selfCrt.Toughness);
                            }
                            else if (ability.Source is Land)
                            {
                                delta -= weights.ManaSources;
                            }
                            else
                            {
                                delta -= weights.KeyCardInPlay * 0.5;
                            }
                            break;
                        case AdditionalCostType.Discard:
                            // Discarding loses one card (-HandSize). Could be a
                            // dead card so this slightly over-charges, but the
                            // outer argmax just needs a tiebreaker not exact EV.
                            delta -= weights.HandSize;
                            break;
                        case AdditionalCostType.Tap:
                            // Tapping a creature source = lose ability to attack /
                            // block this turn (Tempo). Tapping a non-creature
                            // (land, artifact) is essentially free — the source
                            // is built to tap. Don't double-count it.
                            if (ability.Source is Creature)
                            {
                                delta -= weights.Tempo * 0.5;
                            }
                            break;
                    }
                    break;

                case ManaCostCost _:
                    // Mana cost — see <see cref="CostDelta"/> doc: not deducted.
                    break;

                default:
                    // RemoveCounter / SacrificeAnother / DiscardSelf — small
                    // generic penalty. Refinable per-cost in future.
                    delta -= weights.HandSize * 0.25;
                    break;
            }
        }
        return delta;
    }

    // ----- Effect side -----

    /// <summary>Positive delta from the ability's effect. Sniffs the
    /// effect description strings for intent verbs. Falls back to a tiny
    /// Tempo bump when nothing matches, so abilities still beat Pass when
    /// nothing else is competing.</summary>
    private static double EffectDelta(IActivatedAbility ability, GameContext ctx, Player self, ArchetypeWeights weights)
    {
        // ActivatedAbility is the only IActivatedAbility impl today; cast
        // lets us read effect descriptions for intent sniffing. If a future
        // impl shows up without Effects, we fall back to default Tempo.
        if (ability is not ActivatedAbility concrete)
        {
            return weights.Tempo * 0.5;
        }

        var description = string.Join(" | ",
            concrete.Effects.Select(e => (e.Description ?? string.Empty).ToLowerInvariant()));
        var sourceName = SourceName(ability).ToLowerInvariant();
        var combined = description + " | " + sourceName;

        var opp = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, self));
        var oppHasCreature = opp != null
            && opp.Zones.Battlefield.GetCards().OfType<Creature>().Any();
        var oppHasBigThreat = opp != null
            && opp.Zones.Battlefield.GetCards().OfType<Creature>().Any(c => c.Power >= 3);
        var ourCreatures = self.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        var ourCreatureCount = ourCreatures.Count;
        var ourLifeLow = self.LifeTotal <= 8;
        var combatRelevant = CombatRelevant(ctx, self);

        // Removal / burn / wipe — most valuable when opp has board.
        // "destroy target" / "exile target" / "deals N damage" / "return target to hand".
        if (Mentions(combined, "destroy", "exile target", "return target"))
        {
            return oppHasBigThreat
                ? weights.OpponentThreats * -3 + weights.Tempo * 2
                : oppHasCreature
                    ? weights.OpponentThreats * -2 + weights.Tempo
                    : weights.Tempo * 0.25;
        }

        if (Mentions(combined, "deals", "damage"))
        {
            // Burn: damage-to-player / damage-to-creature. Without a parsed
            // amount, assume 1-2 damage (typical pinger). Burn archetype
            // values LifeDelta heavily — Burn weight floats this above the
            // pump alternatives even on low-board states.
            var amount = ExtractInt(combined, fallback: 2);
            if (oppHasBigThreat) return weights.OpponentThreats * -1 + weights.LifeDelta * amount * 0.5;
            return weights.LifeDelta * amount * 0.5 + weights.Tempo * 0.25;
        }

        if (Mentions(combined, "counter target") && !ctx.Stack.IsEmpty)
        {
            return weights.Tempo * 3 + weights.OpponentThreats * -1;
        }

        if (Mentions(combined, "draw"))
        {
            var amount = ExtractInt(combined, fallback: 1);
            return weights.HandSize * amount;
        }

        if (Mentions(combined, "gain") && Mentions(combined, "life"))
        {
            var amount = ExtractInt(combined, fallback: 2);
            return ourLifeLow ? weights.LifeDelta * amount : weights.LifeDelta * amount * 0.4;
        }

        if (Mentions(combined, "+1/+1 counter", "puts a +1/+1", "put a +1/+1"))
        {
            // Pump-style — only valuable when combat is upcoming and we have
            // a creature to put it on. Outside combat phases, no immediate
            // payoff: return 0 so PriorityAction.Pass (which ties at the
            // current eval) wins the strict-greater argmax.
            if (ourCreatureCount == 0) return 0;
            if (!combatRelevant) return 0;
            return weights.BoardPower * 1 + weights.BoardToughness * 1;
        }

        if (Mentions(combined, "gets +", "+x/+x", "gains flying", "gains trample",
                     "gains first strike", "gains lifelink", "gains haste"))
        {
            // Pump / keyword grant — combat-dependent. See "+1/+1 counter"
            // branch above for the zero-return rationale.
            if (ourCreatureCount == 0) return 0;
            if (!combatRelevant) return 0;
            return weights.BoardPower * 1.5;
        }

        if (Mentions(combined, "create", "token"))
        {
            // Token: assume 1/1 vanilla unless number hints at bigger.
            var amount = ExtractInt(combined, fallback: 1);
            return weights.BoardPower * amount + weights.BoardToughness * amount;
        }

        if (Mentions(combined, "search your library"))
        {
            return weights.ManaSources * 0.5 + weights.HandSize * 0.5;
        }

        // Default — tiny tempo bump. Lets a free / mana-only activation
        // beat Pass when nothing else is in the running, but doesn't
        // outscore a real spell.
        return weights.Tempo * 0.3;
    }

    // ----- helpers -----

    /// <summary>True iff combat damage is still ahead in this turn — i.e.
    /// we're at end of opp's combat or earlier on our turn (pre-combat / combat
    /// steps). Pump activations during post-combat / end-step are dead weight.</summary>
    private static bool CombatRelevant(GameContext ctx, Player self)
    {
        var phase = ctx.CurrentPhase;
        // Mid-combat: pump always relevant.
        if (phase == PhaseStateType.DeclareAttackers
            || phase == PhaseStateType.DeclareBlockers
            || phase == PhaseStateType.CombatDamage
            || phase == PhaseStateType.BeginningOfCombat)
        {
            return true;
        }
        // Pre-combat / our main with attackers planned: combat is still
        // ahead this turn. We don't have a "combat plan" oracle here, so
        // approximate as "our turn AND we have untapped creatures AND
        // current phase precedes combat".
        if (ReferenceEquals(ctx.ActivePlayer, self)
            && phase is { } mainPhase && mainPhase.IsMain())
        {
            // Either main phase gates pump here. Pre-combat is the more
            // useful pump window (combat is still ahead); over-grants pumping
            // in post-combat main, but
            // that's the same eager behaviour as today, just bounded by
            // the rest of the cost gate.
            var untappedCreatures = self.Zones.Battlefield.GetCards()
                .OfType<Creature>().Any(c => !c.IsTapped && !c.HasSummoningSickness);
            return untappedCreatures;
        }
        return false;
    }

    private static bool Mentions(string text, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (text.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Pull the first integer out of a description like
    /// "Pay 3 life" or "deals 2 damage". Returns <paramref name="fallback"/>
    /// when none found.</summary>
    private static int ExtractInt(string text, int fallback)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsDigit(text[i]))
            {
                var j = i;
                while (j < text.Length && char.IsDigit(text[j])) j++;
                if (int.TryParse(text.AsSpan(i, j - i), out var v)) return v;
                i = j;
            }
            else i++;
        }
        return fallback;
    }

    private static string SourceName(IActivatedAbility ability)
        => ability.Source is ICard c ? (c.Name ?? string.Empty) : string.Empty;
}
