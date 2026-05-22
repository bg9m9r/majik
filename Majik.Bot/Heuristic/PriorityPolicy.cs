using Majik.Bot.Evaluation;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Bot.Heuristic;

/// <summary>
/// Picks a priority action by enumerating legal options and scoring each
/// via a BoardEval-delta projection. Mirrors the EV-search style used in
/// <see cref="Majik.Bot.Combat.CombatSearch"/>: enumerate candidates,
/// score each against the same archetype weights, take the argmax. Falls
/// back to Pass when no candidate beats the current eval.
/// </summary>
public class PriorityPolicy
{
    protected readonly ArchetypeWeights _weights;

    public PriorityPolicy(ArchetypeWeights weights) { _weights = weights; }

    public virtual PriorityAction Pick(GameContext ctx, Player self)
    {
        var current = BoardEval.Score(ctx, self, _weights);

        PriorityAction best = PriorityAction.Pass;
        double bestScore = current;

        foreach (var (action, projected) in EnumerateCandidates(ctx, self, current))
        {
            if (projected > bestScore)
            {
                bestScore = projected;
                best = action;
            }
        }

        return best;
    }

    /// <summary>
    /// Enumerate legal main-phase priority actions paired with the
    /// projected post-action BoardEval score. Projection is a closed-form
    /// delta over the same components <see cref="BoardEval"/> sums — no
    /// engine mutation. Sorcery-speed actions (PlayLand / non-Instant
    /// CastSpell) are gated on CR 116.2a (active player, Main, empty
    /// stack). PlayLand is also gated on CR 305.2 (one land per turn) —
    /// approximated here by "no land already entered this turn", which
    /// the bot tracks indirectly via the engine's land-drop check; v1
    /// just offers the action and lets the engine reject if illegal.
    /// </summary>
    private IEnumerable<(PriorityAction action, double projected)>
        EnumerateCandidates(GameContext ctx, Player self, double current)
    {
        var sorceryWindow = ctx.ActivePlayer == self
            && ctx.CurrentPhase == PhaseStateType.Main
            && ctx.Stack.Count == 0;

        if (sorceryWindow)
        {
            var landInHand = self.Zones.Hand.GetCards().OfType<Land>().FirstOrDefault();
            if (landInHand != null)
            {
                // Playing a land (CR 305.2) is a free special action — credit
                // the mana-source gain without deducting hand size, since the
                // card converts into a long-term battlefield asset.
                var projected = current + _weights.ManaSources * 1;
                yield return (new PriorityAction.PlayLand(landInHand), projected);
            }
        }

        if (ctx.ActivePlayer == self)
        {
            var manaAvailable = UntappedManaSources(self);
            foreach (var card in self.Zones.Hand.GetCards())
            {
                if (card is Land) continue;
                // Sorcery-speed cards only castable in our main with empty stack.
                if (!IsInstantSpeed(card) && !sorceryWindow) continue;

                var cmc = ApproxCmc(card);
                if (cmc > manaAvailable) continue;

                var projected = current + ProjectCastDelta(card);
                yield return (new PriorityAction.CastSpell(card, Array.Empty<object>()), projected);
            }
        }
    }

    /// <summary>
    /// Closed-form delta to <see cref="BoardEval"/> if the given spell
    /// resolves successfully. Approximates resolution outcome without
    /// running the engine — keeps the search cheap. Mirrors the
    /// component weights used by BoardEval so the deltas commute with
    /// the global eval.
    /// </summary>
    protected double ProjectCastDelta(ICard card)
    {
        // Every cast leaves hand → -1 HandSize.
        double delta = -_weights.HandSize;

        switch (card)
        {
            case Creature crt:
                delta += _weights.BoardPower * Math.Max(0, crt.Power);
                delta += _weights.BoardToughness * Math.Max(0, crt.Toughness);
                // Big creatures count as a "key card" payoff.
                if (crt.Power >= 4) delta += _weights.KeyCardInPlay;
                break;
            case Permanent _:
                // Artifact / Enchantment / Planeswalker — broadly board-positive.
                // Without effect simulation, treat as a small tempo + key-card bump.
                delta += _weights.Tempo * 0.5;
                delta += _weights.KeyCardInPlay * 0.25;
                break;
            default:
                // Instant / Sorcery — one-shot. We can't simulate the effect
                // here, so credit a small tempo bonus weighted by CMC (bigger
                // spell ≈ bigger effect) and nothing else. Burn archetype's
                // LifeDelta-heavy weights still let burn spells fight for the
                // pick via their Tempo contribution.
                delta += _weights.Tempo * Math.Max(1, ApproxCmc(card));
                break;
        }

        return delta;
    }

    /// <summary>Converted mana cost via the engine's parser. Matches the
    /// convention used by <see cref="Majik.Bot.Heuristic.MulliganPolicy"/>
    /// and <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>.</summary>
    protected static int ApproxCmc(ICard card)
        => ManaCost.Parse(card.ManaCost ?? string.Empty).TotalValue;

    /// <summary>Count of untapped lands (rough mana-available proxy).
    /// Matches BoardEval's land-count semantics and keeps the policy
    /// independent of the full mana-payment search.</summary>
    private static int UntappedManaSources(Player self)
        => self.Zones.Battlefield.GetCards().OfType<Land>().Count(l => !l.IsTapped);

    /// <summary>Instant-speed cast eligibility — Instants, and Flash
    /// permanents (CR 702.8). Mirrors <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>.</summary>
    private static bool IsInstantSpeed(ICard c)
    {
        if (c.HasType(CardType.Instant)) return true;
        return c.Abilities.OfType<Majik.Core.Abilities.KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
    }
}
