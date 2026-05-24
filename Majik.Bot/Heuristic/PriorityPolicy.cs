using Majik.Bot.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Core.Abilities;
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
    private readonly IBotDecisionSink _sink;
    private readonly Majik.Core.Diagnostics.VanillaShellTracker? _vanillaTracker;

    /// <summary>Tracks activated-ability IDs we've already fired this turn so
    /// the priority pump doesn't infinite-loop on the same activation. Reset
    /// on turn boundary.</summary>
    private readonly HashSet<Guid> _abilityFiredThisTurn = new();
    private int _abilityMemoTurn = -1;
    private Guid? _lastAbilityProposed;

    public PriorityPolicy(ArchetypeWeights weights)
        : this(weights, NullBotDecisionSink.Instance, vanillaTracker: null) { }

    public PriorityPolicy(ArchetypeWeights weights, IBotDecisionSink sink)
        : this(weights, sink, vanillaTracker: null) { }

    public PriorityPolicy(
        ArchetypeWeights weights,
        IBotDecisionSink sink,
        Majik.Core.Diagnostics.VanillaShellTracker? vanillaTracker)
    {
        _weights = weights;
        _sink = sink ?? NullBotDecisionSink.Instance;
        _vanillaTracker = vanillaTracker;
    }

    public virtual PriorityAction Pick(GameContext ctx, Player self)
    {
        // Turn-boundary reset for the activated-ability memo.
        if (ctx.TurnNumber != _abilityMemoTurn)
        {
            _abilityFiredThisTurn.Clear();
            _lastAbilityProposed = null;
            _abilityMemoTurn = ctx.TurnNumber;
        }
        // If our previous proposal hasn't left the activation stream (e.g.
        // dispatcher rejected it or it resolved silently), mark it fired so
        // we don't re-propose. The engine's PriorityLoop only calls us after
        // the action committed, so the conservative path is to flag whichever
        // we last suggested.
        if (_lastAbilityProposed is Guid prev)
        {
            _abilityFiredThisTurn.Add(prev);
            _lastAbilityProposed = null;
        }

        var current = BoardEval.Score(ctx, self, _weights);

        PriorityAction best = PriorityAction.Pass;
        double bestScore = current;

        // Collect candidates so we can emit alternatives, not just the winner.
        // Materializing the enumerator is cheap — main-phase candidate set is
        // typically <20 items even on huge boards.
        var candidates = new List<(PriorityAction action, double projected, string label)>();
        candidates.Add((PriorityAction.Pass, current, "Pass"));
        foreach (var (action, projected) in EnumerateCandidates(ctx, self, current))
        {
            candidates.Add((action, projected, LabelFor(action)));
            if (projected > bestScore)
            {
                bestScore = projected;
                best = action;
            }
        }

        if (best is PriorityAction.ActivateAbility act)
        {
            _lastAbilityProposed = act.Ability.Id;
        }

        EmitDecision(ctx, self, best, bestScore, candidates);
        return best;
    }

    private void EmitDecision(
        GameContext ctx, Player self,
        PriorityAction chosen, double chosenScore,
        List<(PriorityAction action, double projected, string label)> candidates)
    {
        if (ReferenceEquals(_sink, NullBotDecisionSink.Instance)) return;

        var chosenLabel = LabelFor(chosen);
        var alts = candidates
            .Where(c => !ReferenceEquals(c.action, chosen) && c.label != chosenLabel)
            .OrderByDescending(c => c.projected)
            .Take(3)
            .Select(c => new BotDecisionAlternative(c.label, c.projected))
            .ToList();

        var manaAvailable = UntappedManaSources(self);
        var handSize = self.Zones.Hand.GetCards().Count();
        var ctxFlags = new Dictionary<string, string>
        {
            ["turn"] = ctx.TurnNumber.ToString(),
            ["phase"] = ctx.CurrentPhase?.ToString() ?? "null",
            ["activeIsSelf"] = (ctx.ActivePlayer == self).ToString(),
            ["life"] = self.LifeTotal.ToString(),
            ["hand"] = handSize.ToString(),
            ["manaAvailable"] = manaAvailable.ToString(),
            ["stackSize"] = ctx.Stack.Count.ToString(),
        };
        if (manaAvailable == 0 && handSize > 0
            && self.Zones.Hand.GetCards().Any(c => c is not Land))
        {
            ctxFlags["manaScrew"] = "true";
        }

        try
        {
            _sink.Record(new BotDecision(
                DecisionType: "Priority",
                Chosen: chosenLabel,
                ChosenScore: chosenScore,
                Alternatives: alts,
                Context: ctxFlags));
        }
        catch { /* observer fault must not abort engine */ }
    }

    private static string LabelFor(PriorityAction action) => action switch
    {
        PriorityAction.PassAction _ => "Pass",
        PriorityAction.PlayLand pl => $"PlayLand:{pl.Land.Name}",
        PriorityAction.CastSpell cs => $"CastSpell:{cs.Card.Name}",
        PriorityAction.ActivateAbility aa => $"Activate:{(aa.Ability.Source is ICard c ? c.Name : "?")}",
        _ => action.GetType().Name,
    };

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

                // Vanilla-shell graceful degrade: the engine doesn't enforce
                // this card's rules text, so casting it is mostly a tempo
                // loss (mana + a card from hand for no observable effect
                // beyond zone change for permanents). Notice it (one-shot
                // WARN + bus event) and apply a -CMC EV penalty so the bot
                // only casts it when nothing better is in hand.
                if (card.IsVanillaShell)
                {
                    _vanillaTracker?.Notice(card, self, "castable-spell enumeration");
                }

                var projected = current + ProjectCastDelta(card);
                yield return (new PriorityAction.CastSpell(card, Array.Empty<object>()), projected);
            }
        }

        // Activated abilities of permanents we control (CR 602). Mana
        // abilities are excluded — they aren't priority actions; the
        // ManaPaymentResolver fires them as part of paying a cost. The
        // ActivatedAbilityPolicy projects an EV delta per ability;
        // negative-delta activations stay below `current` and the outer
        // argmax falls through to Pass.
        foreach (var (action, projected) in EnumerateActivatedAbilities(ctx, self, current))
        {
            yield return (action, projected);
        }
    }

    private IEnumerable<(PriorityAction action, double projected)>
        EnumerateActivatedAbilities(GameContext ctx, Player self, double current)
    {
        foreach (var card in self.Zones.Battlefield.GetCards())
        {
            foreach (var ability in card.Abilities.OfType<IActivatedAbility>())
            {
                if (ability is IManaAbility) continue;
                if (_abilityFiredThisTurn.Contains(ability.Id)) continue;
                if (!ability.Costs.All(cost => cost.CanPay(self))) continue;

                var delta = ActivatedAbilityPolicy.ProjectActivateDelta(
                    ability, ctx, self, _weights);
                yield return (
                    new PriorityAction.ActivateAbility(ability, Array.Empty<object>()),
                    current + delta);
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

        // Vanilla-shell graceful degrade (see ICard.IsVanillaShell). The
        // engine doesn't enforce the printed rules text; casting it is a
        // mana + card loss for no observable effect beyond a zone change
        // for permanents. Apply a penalty equal to the CMC so the bot
        // strongly prefers any implemented alternative — but doesn't
        // refuse outright (a future per-card EV override can boost the
        // score for cards with a known good fallback, e.g. a vanilla
        // creature shell where the body alone is worth casting).
        //
        // Vanilla creatures (P/T body is fully enforced) still score
        // their body via the BoardPower/Toughness terms below; the net
        // delta is body − cmc, which mirrors "would I pay X mana for an
        // N/N vanilla?" sanity. Sorceries / instants without an effect
        // sink below 0 — bot defaults to Pass over casting them.
        if (card.IsVanillaShell)
        {
            delta -= ApproxCmc(card);
        }

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
