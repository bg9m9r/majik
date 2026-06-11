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
        // CR 602 + CR 701.23 — land-fetch shape special case. The generic
        // cost/effect split below mis-scores a fetchland crack: CostDelta
        // charges a FULL -ManaSources for sacrificing the land source, but
        // the fetched land replaces it on the battlefield — the sacrifice is
        // mana-NEUTRAL, and the crack adds deck thinning + colour fixing on
        // top. Left to the generic math the delta is always negative
        // (-ManaSources - LifeDelta + tiny effect bump), so the bot played
        // fetches and then never cracked them — effectively one fewer land
        // every game. Score the shape directly instead.
        if (IsLandFetch(ability, out var fetchText))
        {
            return LandFetchDelta(ability, self, fetchText, weights);
        }

        var costDelta = CostDelta(ability, self, weights);
        var effectDelta = EffectDelta(ability, ctx, self, weights);
        return costDelta + effectDelta;
    }

    // ----- Land-fetch shape (fetchlands / Evolving Wilds / Prismatic Vista) -----

    /// <summary>The five basic land types (CR 205.3i) — used to read the
    /// fetch predicate back out of the effect description so the
    /// has-a-target gate can scan the library for a matching land.</summary>
    private static readonly (string Token, CardSubtype Subtype)[] BasicLandTypes =
    {
        ("plains",   CardSubtype.Plains),
        ("island",   CardSubtype.Island),
        ("swamp",    CardSubtype.Swamp),
        ("mountain", CardSubtype.Mountain),
        ("forest",   CardSubtype.Forest),
    };

    /// <summary>
    /// Shape detection: a <see cref="Land"/> source whose activation cost
    /// includes a sacrifice and whose effect searches the library and puts
    /// the result onto the battlefield ("{T}, Pay 1 life, Sacrifice: Search
    /// your library for a … land card, put it onto the battlefield"). Matched
    /// on effect-description text (the same sniffing vocabulary EffectDelta
    /// uses), not card names, so the whole fetch cycle plus Evolving Wilds /
    /// Prismatic Vista shapes all qualify.
    /// </summary>
    private static bool IsLandFetch(IActivatedAbility ability, out string effectText)
    {
        effectText = string.Empty;
        if (ability.Source is not Land) return false;
        if (ability is not ActivatedAbility concrete) return false;
        if (!ability.Costs.OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Sacrifice))
        {
            return false;
        }

        var text = string.Join(" | ",
            concrete.Effects.Select(e => (e.Description ?? string.Empty).ToLowerInvariant()));
        if (!Mentions(text, "search")) return false;
        if (!Mentions(text, "librar")) return false;
        if (!Mentions(text, "battlefield")) return false;

        effectText = text;
        return true;
    }

    /// <summary>
    /// Score a land-fetch activation. Cracking is almost always correct —
    /// mana-neutral (fetched land replaces the sacrificed source), thins the
    /// deck, and fixes colours — so it gets a modest strictly-positive delta
    /// whenever a fetchable land exists in our library (searching our own
    /// library is legal knowledge — CR 701.23). Two hold-backs:
    /// no target in library (pure loss: life + a land for nothing) and
    /// critically low life when the cost includes a life payment.
    /// </summary>
    private static double LandFetchDelta(
        IActivatedAbility ability, Player self, string effectText, ArchetypeWeights weights)
    {
        // Read the fetch predicate back out of the description: which basic
        // land types does it search for? No named type (e.g. "a basic land")
        // → any land in library counts as a target.
        var wantedTypes = BasicLandTypes
            .Where(t => effectText.Contains(t.Token, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Subtype)
            .ToList();

        var libraryLands = self.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();
        var hasTarget = wantedTypes.Count > 0
            ? libraryLands.Any(l => wantedTypes.Any(l.HasSubtype))
            : libraryLands.Count > 0;

        if (!hasTarget)
        {
            // Cracking with no target = pay the costs for nothing. Strongly
            // negative so Pass always wins.
            return -(weights.ManaSources + weights.LifeDelta);
        }

        // Pay-life caution: hold the fetch when the life payment would eat
        // into the last couple of points (e.g. at 2 life vs a pay-1 fetch).
        var lifeCost = ability.Costs.OfType<AdditionalCost>()
            .Where(c => c.CostType == AdditionalCostType.PayLife)
            .Sum(c => ExtractInt(c.Description, fallback: 1));
        if (lifeCost > 0 && self.LifeTotal <= lifeCost + 2)
        {
            return -weights.LifeDelta * lifeCost;
        }

        // Mana-neutral free value: thinning + fixing. Modest positive —
        // reliably beats Pass without outbidding real spells (a fresh land
        // drop still scores higher at ManaSources * 1).
        return weights.ManaSources * 0.5 + weights.Tempo * 0.25;
    }

    /// <summary>
    /// CR 606 — projected BoardEval delta for activating a planeswalker
    /// loyalty ability. Loyalty abilities are their own shape
    /// (<see cref="LoyaltyAbility"/>), pre-pay their cost as loyalty change,
    /// and resolve their effects off the stack. The heuristic: a loyalty
    /// ability is broadly favourable (it protects the walker by adding
    /// loyalty, or spends loyalty for an effect), so we give a baseline
    /// keep-the-walker-active bump plus the same effect-intent sniff used for
    /// activated abilities. Plus / ultimate abilities additionally credit the
    /// loyalty they bank (a more loaded walker is worth more); minus abilities
    /// that remove a threat lean on the effect sniff (destroy / damage), and
    /// the loyalty spent is a mild cost. Always at least slightly positive so
    /// the bot uses its planeswalkers rather than letting them idle.
    /// </summary>
    public static double ProjectLoyaltyDelta(
        LoyaltyAbility ability,
        GameContext ctx,
        Player self,
        ArchetypeWeights weights)
    {
        // Effect-intent sniff over the loyalty ability's own effect
        // descriptions + the source name (same vocabulary as EffectDelta).
        var description = string.Join(" | ",
            ability.Effects.Select(e => (e.Description ?? string.Empty).ToLowerInvariant()));
        var sourceName = (ability.Source.Name ?? string.Empty).ToLowerInvariant();
        var combined = description + " | " + sourceName;

        var opp = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, self));
        var oppHasBigThreat = opp != null
            && opp.Zones.Battlefield.GetCards().OfType<Creature>().Any(c => c.Power >= 3);

        double delta;

        if (ability.LoyaltyChange >= 0)
        {
            // Plus / zero ability: banks loyalty (protects the walker) and
            // generally develops our board. Credit the loyalty gained as a
            // small key-card-stability bump, plus any effect payoff.
            delta = weights.KeyCardInPlay * 0.25 * Math.Max(1, ability.LoyaltyChange + 1);
        }
        else
        {
            // Minus ability: spends loyalty for a (usually stronger) effect.
            // Mild cost for the loyalty burned; the effect sniff supplies the
            // upside (removal against a threat scores high).
            delta = -weights.KeyCardInPlay * 0.1 * -ability.LoyaltyChange;
        }

        // Effect payoff — removal is the high-value case the spec calls out.
        if (Mentions(combined, "destroy", "exile target", "return target", "sacrifice"))
        {
            delta += oppHasBigThreat
                ? weights.OpponentThreats * -2 + weights.Tempo
                : weights.OpponentThreats * -1 + weights.Tempo * 0.5;
        }
        else if (Mentions(combined, "deals", "damage", "loses") && Mentions(combined, "life"))
        {
            delta += weights.LifeDelta * 1.0 + weights.Tempo * 0.25;
        }
        else if (Mentions(combined, "draw"))
        {
            delta += weights.HandSize * ExtractInt(combined, fallback: 1);
        }
        else if (Mentions(combined, "create", "token"))
        {
            delta += weights.BoardPower + weights.BoardToughness;
        }
        else
        {
            delta += weights.Tempo * 0.3;
        }

        return delta;
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

        // Both wordings: oracle text says "search your library"; the engine's
        // generated effect descriptions say "search library for …".
        if (Mentions(combined, "search your library", "search library"))
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
        if (phase == StepStateType.DeclareAttackers
            || phase == StepStateType.DeclareBlockers
            || phase == StepStateType.CombatDamage
            || phase == StepStateType.BeginningOfCombat)
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
