using Majik.Bot.Diagnostics;
using Majik.Bot.Evaluation;
using Majik.Bot.Search;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
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

    /// <summary>CR 305.2 — at most one land per turn. Same anti-spin memo as
    /// the activated-ability one above: once we've proposed a land this turn
    /// we stop offering it, so a rejected/committed land can't have us
    /// re-propose it every priority round. Reset on turn boundary.</summary>
    private bool _landProposedThisTurn;
    private bool _lastWasLandProposal;

    /// <summary>
    /// Fix 1 — anti-spin memo for spell casts. Tracks InstanceIds of cards
    /// we proposed to cast this turn. When a proposed cast silently fails
    /// (mana payment rejected → card rotated back to hand), re-proposing the
    /// same card each priority round spins the loop to the 500-action cap.
    ///
    /// Suppression check: if the card's InstanceId is in this set AND the card
    /// is still in hand (the previous cast didn't actually move it), skip it.
    /// A SUCCESSFUL cast removes the card from hand, so that card's InstanceId
    /// naturally clears from consideration (it's no longer in hand).
    ///
    /// Reset on turn boundary alongside the activated-ability memo.
    /// </summary>
    private readonly HashSet<Guid> _castProposedThisTurn = new();
    private Guid? _lastCastProposed;

    /// <summary>CR 606.3 — loyalty abilities are once-per-turn-per-walker.
    /// The engine's LoyaltyAbilityActivatedThisTurn flag stops the enumerator
    /// re-offering after a successful activation, but a proposal the dispatcher
    /// rejected (raced out of the sorcery window) could otherwise re-spin. Memo
    /// the planeswalker InstanceIds we've proposed a loyalty activation for this
    /// turn. Reset on turn boundary alongside the other memos.</summary>
    private readonly HashSet<Guid> _loyaltyProposedThisTurn = new();
    private Guid? _lastLoyaltyProposed;

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
        // Turn-boundary reset for the activated-ability, land-drop, and cast memos.
        if (ctx.TurnNumber != _abilityMemoTurn)
        {
            _abilityFiredThisTurn.Clear();
            _lastAbilityProposed = null;
            _landProposedThisTurn = false;
            _lastWasLandProposal = false;
            _castProposedThisTurn.Clear();
            _lastCastProposed = null;
            _loyaltyProposedThisTurn.Clear();
            _lastLoyaltyProposed = null;
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
        // Same conservative posture for the land drop: if we proposed a land
        // last time and we're being asked again, the drop is spent (or was
        // rejected) — don't offer it again this turn.
        if (_lastWasLandProposal)
        {
            _landProposedThisTurn = true;
            _lastWasLandProposal = false;
        }
        // Fix 1 — conservative posture for spell casts. If we proposed a cast
        // last time and we're being asked again, AND the card is still in hand
        // (i.e. the cast silently failed / was rotated back), mark that card's
        // InstanceId as already-proposed so EnumerateCandidates suppresses it.
        // We track via _lastCastProposed; the "still in hand" gate lives in
        // EnumerateCandidates so a SUCCESSFUL cast (card leaves hand) naturally
        // clears the suppression — the InstanceId won't be in hand any more.
        if (_lastCastProposed is Guid prevCast)
        {
            _castProposedThisTurn.Add(prevCast);
            _lastCastProposed = null;
        }
        // Same conservative posture for loyalty abilities: if we proposed a
        // loyalty activation last time and we're being asked again, mark that
        // walker as already-proposed so EnumerateCandidates suppresses it.
        if (_lastLoyaltyProposed is Guid prevLoyalty)
        {
            _loyaltyProposedThisTurn.Add(prevLoyalty);
            _lastLoyaltyProposed = null;
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
        else if (best is PriorityAction.PlayLand)
        {
            _lastWasLandProposal = true;
        }
        else if (best is PriorityAction.ActivateLoyaltyAbility la
            && la.Ability.Source is ICard pwCard)
        {
            _lastLoyaltyProposed = pwCard.InstanceId;
        }
        else if (best is PriorityAction.CastSpell cs)
        {
            // Fix 1 — record that we proposed this card's cast. On the NEXT
            // Pick() call the "commit" block above will add it to
            // _castProposedThisTurn, and EnumerateCandidates will suppress
            // re-proposal while the card remains in hand.
            _lastCastProposed = cs.Card.InstanceId;
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
        PriorityAction.ActivateLoyaltyAbility la =>
            $"Loyalty:{(la.Ability.Source is ICard lc ? lc.Name : "?")}{la.Ability.Description}",
        _ => action.GetType().Name,
    };

    /// <summary>
    /// Enumerate legal main-phase priority actions paired with the projected
    /// post-action BoardEval score. Legality is delegated to
    /// <see cref="LegalActionEnumerator.ForPriority"/> (single source of
    /// truth shared with the bot search). Policy-level state filters
    /// (<see cref="_landProposedThisTurn"/>, <see cref="_abilityFiredThisTurn"/>)
    /// are applied here to prevent re-proposal spin loops; the resulting
    /// filtered set is then scored via <see cref="ProjectAction"/>.
    ///
    /// <para>
    /// Projection is a closed-form delta over the same components
    /// <see cref="BoardEval"/> sums — no engine mutation. Sorcery-speed
    /// actions (PlayLand / non-Instant CastSpell) are gated on CR 116.2a
    /// (active player, Main, empty stack). PlayLand is also gated on CR 305.2
    /// (one land per turn) — approximated here by "no land already entered
    /// this turn", which the bot tracks indirectly via the engine's land-drop
    /// check; v1 just offers the action and lets the engine reject if illegal.
    /// </para>
    /// </summary>
    private IEnumerable<(PriorityAction action, double projected)>
        EnumerateCandidates(GameContext ctx, Player self, double current)
    {
        // Delegate legality enumeration to the shared enumerator (spec §10.3).
        // LegalActionEnumerator.ForPriority always includes Pass; we skip it
        // here because Pick() adds Pass unconditionally as the baseline.
        foreach (var action in LegalActionEnumerator.ForPriority(ctx, self))
        {
            if (action is PriorityAction.PassAction) continue;

            // Policy-level anti-spin: suppress re-proposal of a land drop,
            // activated ability, or spell cast that was already proposed this turn.
            if (action is PriorityAction.PlayLand && _landProposedThisTurn)
                continue;
            if (action is PriorityAction.ActivateAbility aa
                && _abilityFiredThisTurn.Contains(aa.Ability.Id))
                continue;
            // Fix 1 — suppress re-proposing a CastSpell whose card is still
            // in hand after a previous failed proposal this turn. A successful
            // cast removes the card from hand, so the card's InstanceId won't
            // appear in hand and this check becomes moot (the LegalActionEnumerator
            // won't enumerate it again).
            if (action is PriorityAction.CastSpell castAction
                && _castProposedThisTurn.Contains(castAction.Card.InstanceId)
                && self.Zones.Hand.GetCards().Any(c => c.InstanceId == castAction.Card.InstanceId))
                continue;
            // CR 606.3 — suppress re-proposing a loyalty ability for a walker
            // we already proposed one for this turn.
            if (action is PriorityAction.ActivateLoyaltyAbility loyaltyAction
                && loyaltyAction.Ability.Source is ICard loyaltyCard
                && _loyaltyProposedThisTurn.Contains(loyaltyCard.InstanceId))
                continue;

            var projected = ProjectAction(action, ctx, self, current);
            yield return (action, projected);
        }
    }

    /// <summary>
    /// Score a single legal action as a projected post-action BoardEval score.
    /// Dispatches to the per-action-type projection helpers.
    /// </summary>
    private double ProjectAction(
        PriorityAction action, GameContext ctx, Player self, double current)
        => action switch
        {
            PriorityAction.PlayLand =>
                // CR 305.2 — playing a land is a free special action. Credit
                // the mana-source gain without deducting hand size since the
                // card converts into a long-term battlefield asset.
                current + _weights.ManaSources * 1,

            PriorityAction.CastSpell cs =>
                ScoreCastSpell(cs.Card, self, current),

            PriorityAction.ActivateAbility aa =>
                // CR 602 — activated abilities. Mana abilities are already
                // excluded by LegalActionEnumerator; only non-mana activations
                // reach here.
                current + ActivatedAbilityPolicy.ProjectActivateDelta(
                    aa.Ability, ctx, self, _weights),

            PriorityAction.ActivateLoyaltyAbility la =>
                // CR 606 — planeswalker loyalty abilities.
                current + ActivatedAbilityPolicy.ProjectLoyaltyDelta(
                    la.Ability, ctx, self, _weights),

            _ => current // conservative: treat unknown action types as neutral
        };

    /// <summary>
    /// Score a CastSpell action. Fires the vanilla-tracker notice (one-shot
    /// WARN + bus event) so the bot's -CMC penalty surfaces in diagnostics,
    /// then delegates to <see cref="ProjectCastDelta"/>.
    /// </summary>
    private double ScoreCastSpell(ICard card, Player self, double current)
    {
        if (card.IsVanillaShell)
            _vanillaTracker?.Notice(card, self, "castable-spell enumeration");
        return current + ProjectCastDelta(card);
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

    /// <summary>Total mana available now: floating pool + untapped mana-source
    /// permanents (lands, dorks, rocks, Treasures). Delegates to the shared
    /// <see cref="LegalActionEnumerator.UntappedManaSources"/> so both the
    /// heuristic policy and the MCTS enumerator use the same count.</summary>
    private static int UntappedManaSources(Player self)
        => LegalActionEnumerator.UntappedManaSources(self);

}
