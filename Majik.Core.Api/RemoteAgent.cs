using Majik.Core.Abilities;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;

namespace Majik.Core.Api;

/// <summary>
/// <see cref="IPlayerAgent"/> that exposes each choice as an awaiting Task.
/// The hosting <see cref="GameFacade"/> calls these from inside the game
/// loop and then waits; the external world (HTTP handler / WebSocket / test)
/// resolves the task by calling <see cref="Submit"/> with a matching command.
///
/// One outstanding prompt at a time. Submitting a command of the wrong kind
/// or from the wrong player throws — the caller is expected to mirror the
/// engine's current expectation.
/// </summary>
public sealed class RemoteAgent : IPlayerAgent
{
    private readonly Player _player;
    private readonly Func<Guid, ICard?>? _cardLookup;
    private readonly Func<Guid, Player?>? _playerLookup;
    private object? _pending;           // TaskCompletionSource<T> for currently-awaited prompt
    private Type[]? _pendingKinds;      // allowed command types (multi for priority)
    // Snapshot of the triggered-ability list the engine handed us at the
    // most recent OrderTriggersAsync prompt. The wire command transports
    // only the StackObjectIds the client picked, so Resolve needs the
    // original list to map IDs back to abilities. Cleared once the prompt
    // resolves (in Submit), or replaced on the next OrderTriggersAsync.
    private IReadOnlyList<ITriggeredAbility>? _pendingTriggerOrder;
    // Engine-supplied candidate list for the most recent library-search
    // prompt (CR 701.19a). Stashed so Submit can validate the picked
    // InstanceId came from the offered set and resolve it back to an
    // ICard. Cleared on prompt resolution; replaced on each new prompt.
    private IReadOnlyList<ICard>? _pendingLibraryCandidates;
    // Engine-supplied peeked-card list for the most recent surveil prompt
    // (CR 701.42). Stashed so Submit can validate that the wire command's
    // partition (ToGraveyard ∪ TopOrder) matches the offered set exactly
    // and resolve each InstanceId back to an ICard. Cleared on prompt
    // resolution; replaced on each new surveil prompt.
    private IReadOnlyList<ICard>? _pendingSurveilPeeked;
    // Engine-supplied eligible-card list for the most recent reveal-and-
    // choose prompt (CR 701.15 — Malevolent Rumble, Impulse, Sleight of
    // Hand, See the Unwritten and friends). Stashed so Submit can resolve
    // the picked InstanceId back to an ICard and reject IDs not in the
    // offered eligible subset. Cleared on prompt resolution; replaced on
    // each new reveal prompt.
    private IReadOnlyList<ICard>? _pendingRevealedEligible;
    // Whether the most recent reveal-and-choose prompt is optional ("you
    // may put"). When false the engine treats a null InstanceId as an
    // agent misbehaviour rather than a legal decline (see Resolve). Reset
    // alongside _pendingRevealedEligible.
    private bool _pendingRevealedOptional;
    // CR 103.4 — required number of cards to put on the bottom of the
    // library for the most recent London-mulligan bottom prompt (equals the
    // number of mulligans taken). Stashed so Resolve can validate the wire
    // ChooseCardsToBottomCommand picks exactly this many cards. Cleared on
    // prompt resolution; replaced on each new bottom prompt.
    private int? _pendingBottomCount;
    // Per-prompt extra payload (currently: library-search candidates +
    // label, surveil peeked view) surfaced via PendingPayload for
    // GameFacade.BuildPrompt to copy into the wire PromptDto. Null on
    // prompt kinds that need no additional context.
    private PromptPayload? _pendingPayload;

    public RemoteAgent(
        Player player,
        Func<Guid, ICard?>? cardLookup = null,
        Func<Guid, Player?>? playerLookup = null)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _cardLookup = cardLookup;
        _playerLookup = playerLookup;
    }

    /// <summary>True iff the engine is currently awaiting a command for this agent.</summary>
    public bool HasPending => _pending != null;

    /// <summary>Type the next submitted command must be (null when no prompt outstanding).</summary>
    public IReadOnlyList<Type>? ExpectedCommandKinds => _pendingKinds;

    /// <summary>
    /// Extra payload the engine attached to the currently-outstanding
    /// prompt (e.g. the library-search candidate list +
    /// human-readable kind label). Null on every prompt that needs no
    /// additional context — the priority window, mulligan, X, mode, etc.
    /// Consumed by <see cref="GameFacade"/> when it builds the wire
    /// <see cref="PromptDto"/>.
    /// </summary>
    public PromptPayload? PendingPayload => _pendingPayload;

    /// <summary>Player slot this agent represents.</summary>
    public Player Player => _player;

    /// <summary>Fires every time the agent transitions from idle to
    /// awaiting input. Subscribers (e.g. transport bridges) receive the
    /// list of command types now legal. Resolved synchronously inside
    /// the engine loop — handlers must not block.</summary>
    public event Action<IReadOnlyList<Type>>? PromptRequested;

    public void Submit(GameCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        if (command.PlayerId != _player.Id)
        {
            throw new InvalidOperationException(
                $"Command targets player {command.PlayerId} but this agent is for player {_player.Id}.");
        }

        if (_pending == null || _pendingKinds == null)
        {
            throw new InvalidOperationException(
                "Agent has no pending prompt; nothing to resolve.");
        }

        if (!_pendingKinds.Contains(command.GetType()))
        {
            var expected = string.Join("/", _pendingKinds.Select(t => t.Name));
            throw new InvalidOperationException(
                $"Engine expected {expected}, got {command.GetType().Name}.");
        }

        var pending = _pending;
        var triggerOrder = _pendingTriggerOrder;
        var libraryCandidates = _pendingLibraryCandidates;
        var surveilPeeked = _pendingSurveilPeeked;
        var revealedEligible = _pendingRevealedEligible;
        var revealedOptional = _pendingRevealedOptional;
        var bottomCount = _pendingBottomCount;
        _pending = null;
        _pendingKinds = null;
        _pendingTriggerOrder = null;
        _pendingLibraryCandidates = null;
        _pendingSurveilPeeked = null;
        _pendingRevealedEligible = null;
        _pendingRevealedOptional = false;
        _pendingBottomCount = null;
        _pendingPayload = null;
        Resolve(pending, command, triggerOrder, libraryCandidates, surveilPeeked, revealedEligible, revealedOptional, bottomCount);
    }

    private void Resolve(
        object tcs,
        GameCommand command,
        IReadOnlyList<ITriggeredAbility>? triggerOrder,
        IReadOnlyList<ICard>? libraryCandidates,
        IReadOnlyList<ICard>? surveilPeeked,
        IReadOnlyList<ICard>? revealedEligible,
        bool revealedOptional,
        int? bottomCount)
    {
        switch (command)
        {
            case PassPriorityCommand:
                ((TaskCompletionSource<PriorityAction>)tcs).SetResult(PriorityAction.Pass);
                break;
            case PlayLandCommand pl:
                var land = ResolveCard(pl.LandInstanceId);
                ((TaskCompletionSource<PriorityAction>)tcs).SetResult(new PriorityAction.PlayLand(land));
                break;
            case CastSpellCommand cs:
                // Resolve the card from any zone (hand is the typical case;
                // graveyard for flashback etc.). TargetInstanceIds / XValue /
                // ModeIndex carried in the priority command are NOT consumed
                // by the engine here — SpellCastFlow prompts the agent for
                // those choices via ChooseTargetsAsync / ChooseXAsync /
                // ChooseModeAsync as a separate envelope (CR 601.2b/c/d).
                // Surface them as PriorityAction.CastSpell.Targets anyway so
                // a future opt-in path that pre-resolves targets (kept for
                // bot agents that already build full plans) can use the
                // metadata if needed.
                var card = ResolveCard(cs.CardInstanceId);
                var resolvedTargets = cs.TargetInstanceIds.Count == 0
                    ? (IReadOnlyList<object>)Array.Empty<object>()
                    : cs.TargetInstanceIds.Select(id => (object)ResolveCard(id)).ToList();
                ((TaskCompletionSource<PriorityAction>)tcs).SetResult(
                    new PriorityAction.CastSpell(card, resolvedTargets));
                break;
            case MulliganCommand m:
                ((TaskCompletionSource<MulliganDecision>)tcs).SetResult(
                    m.Keep ? MulliganDecision.Keep : MulliganDecision.Mulligan);
                break;
            case ChooseTargetsCommand t:
                ((TaskCompletionSource<IReadOnlyList<object>>)tcs).SetResult(
                    t.TargetInstanceIds.Select(id => (object)ResolveCard(id)).ToList());
                break;
            case ChooseXCommand x:
                ((TaskCompletionSource<int>)tcs).SetResult(x.X);
                break;
            case ChooseModeCommand mc:
                ((TaskCompletionSource<int>)tcs).SetResult(mc.ModeIndex);
                break;
            case ChooseManaCommand mp:
                ((TaskCompletionSource<ManaPayment>)tcs).SetResult(
                    new ManaPayment(mp.SourceInstanceIds.Select(ResolveCard).ToList()));
                break;
            case CancelCastCommand:
                // CR 601.2 / CR 727 — bailing out of a cast at the cost-
                // payment step. Surface the Cancelled sentinel so the
                // dispatch site can refund any pool-deducted mana and
                // leave the spell in hand. Validity of the prompt context
                // (must be a ChooseManaCommand awaiting) is enforced by
                // _pendingKinds in Submit — a CancelCastCommand sent at
                // any other prompt is rejected before reaching here.
                ((TaskCompletionSource<ManaPayment>)tcs).SetResult(ManaPayment.Cancelled);
                break;
            case ActivateManaAbilityCommand ama:
            {
                // CR 605 — translate the wire command into the engine's
                // PriorityAction.ActivateManaAbility. The source must be a
                // Permanent the caller controls; we resolve it via the
                // lookup, pick the matching IManaAbility by colour, and
                // surface a clear InvalidOperationException on any mismatch
                // (no source, not a permanent, wrong controller, no mana
                // ability, ambiguous when Color is empty, no colour match).
                var source = ResolveCard(ama.PermanentInstanceId);
                if (source is not Permanent permanent)
                {
                    throw new InvalidOperationException(
                        $"ActivateManaAbilityCommand source {ama.PermanentInstanceId} is not a Permanent ({source.GetType().Name}).");
                }
                if (permanent.Controller != null && !ReferenceEquals(permanent.Controller, _player))
                {
                    throw new InvalidOperationException(
                        $"Player {_player.Id} does not control permanent {permanent.Name}.");
                }
                var manaAbilities = permanent.Abilities.OfType<IManaAbility>().ToList();
                if (manaAbilities.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Permanent {permanent.Name} has no mana abilities.");
                }
                var chosen = PickManaAbility(permanent, manaAbilities, ama.Color);
                ((TaskCompletionSource<PriorityAction>)tcs).SetResult(
                    new PriorityAction.ActivateManaAbility(permanent, chosen));
                break;
            }
            case ActivateAbilityCommand aac:
            {
                // CR 602 — translate the wire command into the engine's
                // PriorityAction.ActivateAbility. Validate locally only the
                // bits that gate command-routing (source exists, is a
                // permanent the caller controls, has the named activated
                // ability). Cost-payability, zone gates, sorcery-speed
                // riders, target legality, etc. are all engine concerns
                // and are validated by AbilityActivator / ActionValidator
                // when GameFacade.DispatchActivate runs the activation.
                var source = ResolveCard(aac.PermanentInstanceId);
                if (source is not Permanent permanent)
                {
                    throw new InvalidOperationException(
                        $"ActivateAbilityCommand source {aac.PermanentInstanceId} is not a Permanent ({source.GetType().Name}).");
                }
                if (permanent.Controller != null && !ReferenceEquals(permanent.Controller, _player))
                {
                    throw new InvalidOperationException(
                        $"Player {_player.Id} does not control permanent {permanent.Name}.");
                }
                var ability = permanent.Abilities
                    .OfType<IActivatedAbility>()
                    .Where(a => a is not IManaAbility)
                    .FirstOrDefault(a => a.Id == aac.AbilityId);
                if (ability == null)
                {
                    throw new InvalidOperationException(
                        $"Permanent {permanent.Name} has no non-mana activated ability with id {aac.AbilityId}.");
                }
                // Targets are not pre-resolved here — the activation flow
                // re-prompts via ChooseTargetsAsync (mirrors the
                // CastSpellCommand posture above; CR 602.1b chooses targets
                // as part of activation, not the priority command).
                ((TaskCompletionSource<PriorityAction>)tcs).SetResult(
                    new PriorityAction.ActivateAbility(ability, Array.Empty<object>()));
                break;
            }
            case OrderTriggersCommand ot:
                // CR 603.3b — APNAP-controller orders their own simultaneous
                // triggers onto the stack. The wire command carries only
                // stack-object IDs; map each back to the ability handed to
                // us by the engine at prompt time. Same shape as
                // DeclareAttackersCommand (PR #154) and MulliganCommand
                // (PR #147): translate the wire DTO into the engine's
                // expected payload (here IReadOnlyList<ITriggeredAbility>).
                if (triggerOrder == null)
                {
                    throw new InvalidOperationException(
                        "OrderTriggersCommand resolved without a pending trigger order.");
                }
                var byId = triggerOrder.ToDictionary(t => t.Id);
                var orderedTriggers = new List<ITriggeredAbility>(ot.StackObjectIdsInOrder.Count);
                foreach (var id in ot.StackObjectIdsInOrder)
                {
                    if (!byId.TryGetValue(id, out var ability))
                    {
                        throw new InvalidOperationException(
                            $"OrderTriggersCommand referenced unknown stack object {id}.");
                    }
                    orderedTriggers.Add(ability);
                }
                if (orderedTriggers.Count != triggerOrder.Count)
                {
                    throw new InvalidOperationException(
                        $"OrderTriggersCommand listed {orderedTriggers.Count} triggers " +
                        $"but engine expected {triggerOrder.Count}.");
                }
                ((TaskCompletionSource<IReadOnlyList<ITriggeredAbility>>)tcs).SetResult(orderedTriggers);
                break;
            case DeclareAttackersCommand atk:
                ((TaskCompletionSource<CombatPlan>)tcs).SetResult(BuildCombatPlan(atk));
                break;
            case DeclareBlockersCommand blk:
                ((TaskCompletionSource<BlockPlan>)tcs).SetResult(BuildBlockPlan(blk));
                break;
            case ChooseSurveilCommand sc:
            {
                // CR 701.42 — partition the peeked top-N into graveyard /
                // top-order buckets and ship a SurveilDecision back to the
                // engine. Validate that the wire payload covers the peeked
                // set exactly once (no duplicates, no extras, no missing
                // peeked card) so the client can't smuggle library order
                // beyond what the engine offered.
                if (surveilPeeked == null)
                {
                    throw new InvalidOperationException(
                        "ChooseSurveilCommand resolved without a pending peeked-card list.");
                }
                var surveilById = surveilPeeked.ToDictionary(c => c.InstanceId);
                var seen = new HashSet<Guid>();
                var toGy = new List<ICard>(sc.ToGraveyardInstanceIds.Count);
                foreach (var id in sc.ToGraveyardInstanceIds)
                {
                    if (!surveilById.TryGetValue(id, out var gyCard))
                        throw new InvalidOperationException(
                            $"ChooseSurveilCommand ToGraveyard references unknown instance {id}.");
                    if (!seen.Add(id))
                        throw new InvalidOperationException(
                            $"ChooseSurveilCommand listed instance {id} more than once.");
                    toGy.Add(gyCard);
                }
                var top = new List<ICard>(sc.TopOrderInstanceIds.Count);
                foreach (var id in sc.TopOrderInstanceIds)
                {
                    if (!surveilById.TryGetValue(id, out var topCard))
                        throw new InvalidOperationException(
                            $"ChooseSurveilCommand TopOrder references unknown instance {id}.");
                    if (!seen.Add(id))
                        throw new InvalidOperationException(
                            $"ChooseSurveilCommand listed instance {id} more than once.");
                    top.Add(topCard);
                }
                if (seen.Count != surveilPeeked.Count)
                {
                    throw new InvalidOperationException(
                        $"ChooseSurveilCommand partitioned {seen.Count} cards but engine peeked {surveilPeeked.Count}.");
                }
                ((TaskCompletionSource<SurveilAction.SurveilDecision>)tcs).SetResult(
                    new SurveilAction.SurveilDecision(toGy, top));
                break;
            }
            case ChooseYesNoCommand yn:
                // CR 117.x / 605.1 — translate the wire bool answer back
                // to the engine's TaskCompletionSource<bool>. Submit()
                // already enforced _pendingKinds so we know the caller
                // intended this prompt; nothing further to validate.
                ((TaskCompletionSource<bool>)tcs).SetResult(yn.Answer);
                break;
            case ChooseFromRevealedCommand cr:
            {
                // CR 701.15 — translate the wire command into the ICard the
                // engine's reveal-and-choose effect expects, or null for
                // "decline" (only legal when the prompt was optional OR
                // the eligible set was empty). Verify the pick is from the
                // engine-offered eligible subset so the client can't smuggle
                // an arbitrary revealed-but-ineligible card past the filter.
                //
                // Server-side validation posture (per spec): if the wire
                // submits an InstanceId not in the eligible set, return
                // null + log to console rather than throwing. The brief
                // explicitly calls this out so a malicious / buggy client
                // can't crash a live match — better to no-op the pick.
                if (revealedEligible == null)
                {
                    throw new InvalidOperationException(
                        "ChooseFromRevealedCommand resolved without a pending eligible-card list.");
                }
                ICard? picked = null;
                if (cr.InstanceId is Guid pickedId)
                {
                    picked = revealedEligible.FirstOrDefault(c => c.InstanceId == pickedId);
                    if (picked == null)
                    {
                        // Out-of-set pick — coerce to decline + warn. The
                        // engine sees the same null payload it would on a
                        // legitimate decline, but the log surfaces the
                        // client misbehaviour for diagnostics.
                        Console.Error.WriteLine(
                            $"WARN: ChooseFromRevealedCommand selected instance {pickedId} " +
                            $"is not in the offered eligible list — treating as decline.");
                    }
                }
                // When the prompt was MANDATORY (Optional=false) AND the
                // eligible set is non-empty, a null pick is an agent
                // misbehaviour, not a legal decline. Fall back to the first
                // eligible card so the engine doesn't see a no-op on a
                // "put one of them" clause. When eligible is empty, null
                // is always legal — mandatory clauses can't force a pick
                // from nothing.
                if (picked == null && !revealedOptional && revealedEligible.Count > 0)
                {
                    Console.Error.WriteLine(
                        "WARN: ChooseFromRevealedCommand declined on a mandatory prompt " +
                        "with non-empty eligible set — falling back to first eligible.");
                    picked = revealedEligible[0];
                }
                ((TaskCompletionSource<ICard?>)tcs).SetResult(picked);
                break;
            }
            case ChooseLibraryPickCommand lp:
            {
                // CR 701.19a — translate the wire command into the
                // ICard the engine's search effect expects, or null for
                // "find nothing". Verify the pick is from the engine-
                // offered candidate set so the client can't smuggle a
                // pick of an arbitrary library card (which would bypass
                // the search predicate — e.g. tutoring a non-green card
                // for Green Sun's Zenith). Submit() already enforced
                // _pendingKinds, so we know libraryCandidates was set
                // when this prompt fired.
                if (libraryCandidates == null)
                {
                    throw new InvalidOperationException(
                        "ChooseLibraryPickCommand resolved without a pending candidate list.");
                }
                ICard? picked = null;
                if (lp.SelectedInstanceId is Guid id)
                {
                    picked = libraryCandidates.FirstOrDefault(c => c.InstanceId == id);
                    if (picked == null)
                    {
                        throw new InvalidOperationException(
                            $"ChooseLibraryPickCommand selected instance {id} is not in the offered candidate list.");
                    }
                }
                ((TaskCompletionSource<ICard?>)tcs).SetResult(picked);
                break;
            }
            case ChooseCardsToBottomCommand cb:
            {
                // CR 103.4 — after a London mulligan the player puts N cards
                // on the bottom of their library, where N = mulligans taken.
                // Resolve each wire instance id to an ICard, then validate the
                // pick before applying ANYTHING (no partial application):
                //   1. the count must equal the required bottom count for the
                //      pending prompt (the portal gates to exactly N; this is
                //      server-side defence — surfaces as 400 invalid-command);
                //   2. every chosen card must currently be in the player's hand
                //      (the client can't smuggle a card from another zone onto
                //      the bottom of the library).
                // Pre-fix this command had no case here and fell through to the
                // default throw, which MatchService turned into HTTP 400
                // invalid-command — the mulligan flow (MulliganController
                // awaiting ChooseCardsToBottomAsync) never completed and the
                // game stuck.
                if (bottomCount is not int required)
                {
                    throw new InvalidOperationException(
                        "ChooseCardsToBottomCommand resolved without a pending bottom count.");
                }
                if (cb.CardInstanceIds.Count != required)
                {
                    throw new InvalidOperationException(
                        $"ChooseCardsToBottomCommand listed {cb.CardInstanceIds.Count} cards " +
                        $"but the player must put exactly {required} on the bottom.");
                }
                var chosen = new List<ICard>(cb.CardInstanceIds.Count);
                foreach (var id in cb.CardInstanceIds)
                {
                    var resolved = ResolveCard(id);
                    if (!_player.Zones.Hand.ContainsCard(resolved))
                    {
                        throw new InvalidOperationException(
                            $"ChooseCardsToBottomCommand card {id} is not in the player's hand.");
                    }
                    chosen.Add(resolved);
                }
                ((TaskCompletionSource<IReadOnlyList<ICard>>)tcs).SetResult(chosen);
                break;
            }
            default:
                throw new InvalidOperationException($"Unhandled command {command.GetType().Name}.");
        }
    }

    /// <summary>
    /// CR 605 — pick which mana ability on <paramref name="permanent"/> the
    /// wire command's colour code (W/U/B/R/G/C) maps to. Empty string
    /// resolves to the sole ability when there is exactly one; otherwise
    /// requires an exact colour match. Throws if no ability produces the
    /// requested colour or if the empty-string shortcut is ambiguous.
    /// </summary>
    private static IManaAbility PickManaAbility(
        Permanent permanent,
        IReadOnlyList<IManaAbility> abilities,
        string color)
    {
        if (string.IsNullOrEmpty(color))
        {
            if (abilities.Count == 1) return abilities[0];
            throw new InvalidOperationException(
                $"Permanent {permanent.Name} has {abilities.Count} mana abilities; " +
                "ActivateManaAbilityCommand.Color is required to disambiguate.");
        }

        var normalized = color.Trim().ToUpperInvariant();
        var matches = abilities.Where(a => ManaAbilityProducesColor(a, normalized)).ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Permanent {permanent.Name} has no mana ability producing {{{normalized}}}.");
        }
        return matches[0];
    }

    /// <summary>Single-symbol colour-of-produced-mana test used by
    /// <see cref="PickManaAbility"/>. Only the five WUBRG colours plus C
    /// (colourless, modelled as Generic) are supported in v1.</summary>
    private static bool ManaAbilityProducesColor(IManaAbility ability, string color)
    {
        var mc = ability.ManaGenerated;
        if (mc == null) return false;
        return color switch
        {
            "W" => mc.White > 0,
            "U" => mc.Blue > 0,
            "B" => mc.Black > 0,
            "R" => mc.Red > 0,
            "G" => mc.Green > 0,
            // {C} is currently stored under Generic (see ManaCost.Parse).
            "C" => mc.Generic > 0 && mc.White == 0 && mc.Blue == 0
                && mc.Black == 0 && mc.Red == 0 && mc.Green == 0,
            _ => false,
        };
    }

    private ICard ResolveCard(Guid id)
    {
        if (_cardLookup == null)
        {
            throw new InvalidOperationException(
                "RemoteAgent has no card lookup; cannot resolve card instance ID.");
        }

        return _cardLookup(id)
            ?? throw new InvalidOperationException($"No card found for instance {id}.");
    }

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => Prompt<PriorityAction>(ct, PriorityKinds.Build(ctx));

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => Prompt<MulliganDecision>(ct, typeof(MulliganCommand));

    /// <summary>
    /// CR 103.4 — after a London mulligan the player puts
    /// <paramref name="countToBottom"/> cards (= mulligans taken) on the
    /// bottom of their library. The base <see cref="IPlayerAgent"/> default
    /// auto-bottoms the first N, which silently picked for remote (human)
    /// players. This override stashes the required count on the prompt
    /// payload (so the portal can render the "bottom N card(s)" label and
    /// gate to exactly N) and awaits a <see cref="ChooseCardsToBottomCommand"/>
    /// back from the client. The command's <c>CardInstanceIds</c> are
    /// validated against the count + the player's hand in
    /// <see cref="Resolve"/> before the cards are bottomed.
    /// </summary>
    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
    {
        // Mirror ChooseLibraryPickAsync / ChooseSurveilDecisionAsync: stash
        // before Prompt fires PromptRequested observers, guard by checking
        // _pending so we don't smear stash on top of a still-pending prompt.
        if (_pending != null)
        {
            throw new InvalidOperationException("A prompt is already pending.");
        }
        _pendingBottomCount = countToBottom;
        _pendingPayload = new PromptPayload(
            Candidates: null,
            Label: countToBottom == 1 ? "bottom 1 card" : $"bottom {countToBottom} cards",
            BottomCount: countToBottom);
        try
        {
            return Prompt<IReadOnlyList<ICard>>(ct, typeof(ChooseCardsToBottomCommand));
        }
        catch
        {
            _pendingBottomCount = null;
            _pendingPayload = null;
            throw;
        }
    }

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => Prompt<IReadOnlyList<object>>(ct, typeof(ChooseTargetsCommand));

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => Prompt<int>(ct, typeof(ChooseXCommand));

    public Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null,
        CancellationToken ct = default)
        => Prompt<int>(ct, typeof(ChooseModeCommand));

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
    {
        // Stash the engine-provided list so the eventual
        // OrderTriggersCommand can translate its StackObjectIds back into
        // the matching ability instances. Replaced on each prompt; cleared
        // in Submit when the prompt resolves. Only commit the field if
        // Prompt actually accepts the registration (it throws if another
        // prompt is already outstanding).
        try
        {
            var task = Prompt<IReadOnlyList<ITriggeredAbility>>(ct, typeof(OrderTriggersCommand));
            _pendingTriggerOrder = mine;
            return task;
        }
        catch
        {
            _pendingTriggerOrder = null;
            throw;
        }
    }

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        // CR 601.2 / CR 727 — CancelCastCommand is offered alongside
        // ChooseManaCommand at the cost-payment prompt so the remote
        // (human) player can back out before any mana is spent. The
        // resolver translates Cancelled → no-op cast at the dispatch site.
        => Prompt<ManaPayment>(ct, typeof(ChooseManaCommand), typeof(CancelCastCommand));

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => Prompt<CombatPlan>(ct, typeof(DeclareAttackersCommand));

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        => Prompt<BlockPlan>(ct, typeof(DeclareBlockersCommand));

    // CR 508.1 — translate the wire DeclareAttackersCommand into the
    // engine-shaped CombatPlan. Each declaration carries an attacker (must
    // be a Creature this agent controls) and a defender (either a Player
    // id or a Planeswalker InstanceId on the battlefield). We resolve the
    // attacker via _cardLookup and the defender by trying _playerLookup
    // first, then falling back to _cardLookup expecting a Planeswalker.
    // Empty Attackers list = "attack with nothing", which is a legal plan
    // and falls out naturally (CR 508.2 — declaring no attackers is fine).
    private CombatPlan BuildCombatPlan(DeclareAttackersCommand cmd)
    {
        if (cmd.Attackers.Count == 0)
        {
            return CombatPlan.None;
        }

        var decls = new List<AttackerDeclaration>(cmd.Attackers.Count);
        foreach (var dto in cmd.Attackers)
        {
            var card = ResolveCard(dto.AttackerInstanceId);
            if (card is not Creature creature)
            {
                throw new InvalidOperationException(
                    $"Attacker {dto.AttackerInstanceId} is not a Creature ({card.GetType().Name}).");
            }

            var defender = ResolveDefender(dto.DefenderId);
            decls.Add(new AttackerDeclaration(creature, defender));
        }
        return new CombatPlan(decls);
    }

    // CR 509.1 — translate the wire DeclareBlockersCommand. Each blocker
    // must be a Creature this agent controls; each attacker (referenced
    // by InstanceId) must be a Creature on the battlefield. Multiple
    // blockers may target the same attacker — declaration order in the
    // wire list determines damage-assignment order downstream
    // (CR 509.2 — the defending player orders blockers when declaring).
    private BlockPlan BuildBlockPlan(DeclareBlockersCommand cmd)
    {
        if (cmd.Blockers.Count == 0)
        {
            return BlockPlan.None;
        }

        var decls = new List<BlockerDeclaration>(cmd.Blockers.Count);
        foreach (var dto in cmd.Blockers)
        {
            var blockerCard = ResolveCard(dto.BlockerInstanceId);
            if (blockerCard is not Creature blocker)
            {
                throw new InvalidOperationException(
                    $"Blocker {dto.BlockerInstanceId} is not a Creature ({blockerCard.GetType().Name}).");
            }

            var attackerCard = ResolveCard(dto.AttackerInstanceId);
            if (attackerCard is not Creature attacker)
            {
                throw new InvalidOperationException(
                    $"Attacker {dto.AttackerInstanceId} is not a Creature ({attackerCard.GetType().Name}).");
            }

            decls.Add(new BlockerDeclaration(blocker, attacker));
        }
        return new BlockPlan(decls);
    }

    private object ResolveDefender(Guid defenderId)
    {
        var player = _playerLookup?.Invoke(defenderId);
        if (player != null) return player;

        // Fall back to planeswalker on the battlefield.
        var card = _cardLookup?.Invoke(defenderId);
        if (card is Majik.Core.Cards.Planeswalker pw) return pw;

        throw new InvalidOperationException(
            $"Defender {defenderId} is neither a known player nor a Planeswalker.");
    }

    // TODO (v2): wire the Scry prompt through the command channel
    // (ChooseScryCommand) once the prompt system is updated to handle
    // sync-over-async in effect closures.
    public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => throw new NotImplementedException("Scry prompt wired in v2 (effect async refactor).");

    /// <summary>
    /// CR 701.42 — surveil prompt. Default in <see cref="IPlayerAgent"/>
    /// auto-keeps all peeked cards on top, which caused remote (human)
    /// players to see DSK surveil lands ETB and silently send nothing to
    /// the graveyard (pre-fix RemoteAgent threw NotImplementedException —
    /// the engine then crashed the resolve closure under the
    /// GetAwaiter().GetResult() bridge). Override snapshots the engine-
    /// peeked top-N onto the prompt payload so the portal can render the
    /// surveil modal, then awaits a <see cref="ChooseSurveilCommand"/> back
    /// from the client. The command's <c>ToGraveyardInstanceIds</c> /
    /// <c>TopOrderInstanceIds</c> are validated against the peeked set in
    /// <see cref="Resolve"/> before constructing the
    /// <see cref="SurveilAction.SurveilDecision"/>.
    /// </summary>
    public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> peeked,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(peeked);
        // Mirror ChooseLibraryPickAsync: stash before Prompt fires
        // PromptRequested observers, guard by checking _pending so we don't
        // smear stash on top of a still-pending prompt.
        if (_pending != null)
        {
            throw new InvalidOperationException("A prompt is already pending.");
        }
        var snapshots = peeked.Select(StateSnapshotter.SnapshotCard).ToList();
        _pendingSurveilPeeked = peeked;
        _pendingPayload = new PromptPayload(
            Candidates: null,
            Label: peeked.Count == 1 ? "surveil 1" : $"surveil {peeked.Count}",
            LibraryView: null,
            SurveilView: snapshots);
        try
        {
            return Prompt<SurveilAction.SurveilDecision>(ct, typeof(ChooseSurveilCommand));
        }
        catch
        {
            _pendingSurveilPeeked = null;
            _pendingPayload = null;
            throw;
        }
    }

    /// <summary>
    /// CR 701.19a — library search. Default in <see cref="IPlayerAgent"/>
    /// auto-picks <c>candidates[0]</c>, which caused remote (human) users
    /// to see Green Sun's Zenith / Mystical Tutor / Path to Exile etc.
    /// resolve silently without any UI. Override snapshots the engine-
    /// filtered candidates onto the prompt payload so the portal can
    /// render a searchable list, then awaits a
    /// <see cref="ChooseLibraryPickCommand"/> back from the client. The
    /// command's <c>SelectedInstanceId</c> is resolved against the
    /// candidate set in <see cref="Resolve"/>; a <see langword="null"/>
    /// id models the legal "find nothing" branch.
    /// </summary>
    public Task<ICard?> ChooseLibraryPickAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> candidates,
        string kindLabel,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        // Stash candidates + payload BEFORE Prompt invokes PromptRequested
        // observers — GameFacade.BuildPrompt reads PendingPayload
        // synchronously from inside the observer to populate the wire
        // PromptDto's Candidates/Label fields. If Prompt threw because a
        // prompt is already outstanding (_pending != null), we'd be
        // smearing our stash on top of the prior prompt's state — guard
        // by checking _pending first, mirroring the same gate Prompt uses.
        if (_pending != null)
        {
            throw new InvalidOperationException("A prompt is already pending.");
        }
        var snapshots = candidates.Select(StateSnapshotter.SnapshotCard).ToList();
        // CR 701.19a — while a player is searching their library, that player
        // may look at it. Snapshot the full library (top-to-bottom order) so
        // the portal can render a deck-flip view with candidates highlighted
        // and ineligible cards muted. The prompt is published only to the
        // searching player (per-recipient SignalR routing), so the opponent
        // never sees the library order.
        var libraryView = _player.Zones.Library.GetCards()
            .Select(StateSnapshotter.SnapshotCard)
            .ToList();
        _pendingLibraryCandidates = candidates;
        _pendingPayload = new PromptPayload(
            Candidates: snapshots,
            Label: kindLabel,
            LibraryView: libraryView);
        try
        {
            return Prompt<ICard?>(ct, typeof(ChooseLibraryPickCommand));
        }
        catch
        {
            _pendingLibraryCandidates = null;
            _pendingPayload = null;
            throw;
        }
    }

    /// <summary>
    /// CR 701.15 — reveal-and-choose prompt (Malevolent Rumble, Impulse,
    /// Sleight of Hand, See the Unwritten, …). Default in
    /// <see cref="IPlayerAgent"/> auto-picks the first eligible card,
    /// which causes remote (human) users to see the spell resolve
    /// silently. Override snapshots every revealed card (full set —
    /// so the portal can render the reveal pile) plus the eligible
    /// subset's instance IDs onto the prompt payload, then awaits a
    /// <see cref="ChooseFromRevealedCommand"/> back from the client.
    /// The command's <c>InstanceId</c> is resolved against the eligible
    /// set in <see cref="Resolve"/>; a <see langword="null"/> id models
    /// the legal "decline" branch (only legal when
    /// <paramref name="optional"/> = true OR <paramref name="eligible"/>
    /// is empty).
    /// <para>
    /// Empty eligible still fires the prompt so the player sees the
    /// reveal — matches the "search prompt on empty candidates" UX
    /// principle from <see cref="ChooseLibraryPickAsync"/> (the silent-
    /// no-op bug we already fixed for tutors).
    /// </para>
    /// </summary>
    public Task<ICard?> ChooseFromRevealedAsync(
        GameContext? ctx,
        IReadOnlyList<ICard> revealed,
        IReadOnlyList<ICard> eligible,
        bool optional,
        string label,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(revealed);
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(label);
        // Mirror ChooseLibraryPickAsync / ChooseSurveilDecisionAsync: stash
        // before Prompt fires PromptRequested observers, guard by checking
        // _pending so we don't smear stash on top of a still-pending prompt.
        if (_pending != null)
        {
            throw new InvalidOperationException("A prompt is already pending.");
        }
        var revealedSnapshots = revealed.Select(StateSnapshotter.SnapshotCard).ToList();
        var eligibleIds = new HashSet<Guid>(eligible.Select(c => c.InstanceId));
        _pendingRevealedEligible = eligible;
        _pendingRevealedOptional = optional;
        _pendingPayload = new PromptPayload(
            Candidates: null,
            Label: label,
            LibraryView: null,
            SurveilView: null,
            YesNoView: null,
            RevealView: new RevealView(
                Revealed: revealedSnapshots,
                EligibleInstanceIds: eligibleIds,
                Optional: optional,
                Label: label));
        try
        {
            return Prompt<ICard?>(ct, typeof(ChooseFromRevealedCommand));
        }
        catch
        {
            _pendingRevealedEligible = null;
            _pendingRevealedOptional = false;
            _pendingPayload = null;
            throw;
        }
    }

    /// <summary>
    /// CR 117.x / 605.1 — wire-shaped Yes/No prompt. Default in
    /// <see cref="IPlayerAgent"/> delegates to the legacy intent-driven
    /// overload which is fine for bot agents but auto-decides for remote
    /// (human) players, so this override stashes a
    /// <see cref="YesNoViewDto"/> on the prompt payload and awaits a
    /// <see cref="ChooseYesNoCommand"/> back from the client.
    /// </summary>
    public Task<bool> ChooseYesNoAsync(
        GameContext? ctx,
        string question,
        string? sourceCardName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question must be non-empty.", nameof(question));
        // Mirror ChooseLibraryPickAsync / ChooseSurveilDecisionAsync: stash
        // before Prompt fires PromptRequested observers, guard by checking
        // _pending so we don't smear stash on top of a still-pending prompt.
        if (_pending != null)
        {
            throw new InvalidOperationException("A prompt is already pending.");
        }
        _pendingPayload = new PromptPayload(
            Candidates: null,
            Label: null,
            LibraryView: null,
            SurveilView: null,
            YesNoView: new YesNoViewDto(
                Question: question,
                SourceCardName: sourceCardName));
        try
        {
            return Prompt<bool>(ct, typeof(ChooseYesNoCommand));
        }
        catch
        {
            _pendingPayload = null;
            throw;
        }
    }

    private Task<T> Prompt<T>(CancellationToken ct, params Type[] acceptedKinds)
    {
        if (_pending != null)
        {
            throw new InvalidOperationException("A prompt is already pending.");
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetCanceled(ct));
        _pending = tcs;
        _pendingKinds = acceptedKinds;

        try { PromptRequested?.Invoke(acceptedKinds); }
        catch { /* observer fault must not abort the engine */ }

        return tcs.Task;
    }
}

/// <summary>
/// Extra per-prompt context the engine attaches to an outstanding
/// <see cref="RemoteAgent"/> prompt. Currently used for library-search
/// (CR 701.19a) so the wire <see cref="PromptDto"/> can ship the
/// engine-filtered candidate list + a human-readable kind label without
/// the portal having to re-derive either client-side.
/// </summary>
public sealed record PromptPayload(
    IReadOnlyList<CardSnapshotDto>? Candidates = null,
    string? Label = null,
    /// <summary>
    /// Full snapshot of the searching player's library (top-to-bottom order)
    /// at the time the search prompt fires (CR 701.19a — while searching, a
    /// player may look at their own library). Non-null only on library-search
    /// prompts; null on every other prompt kind.
    /// <para>
    /// <c>Candidates.Select(c =&gt; c.InstanceId)</c> is the engine-filtered
    /// eligible subset — the portal highlights those cards and mutes the rest
    /// so it renders like flipping through the deck.
    /// </para>
    /// <para>
    /// Privacy: the prompt envelope is published only to the searching player
    /// (per-recipient SignalR routing). The full library order is never
    /// broadcast to the opponent or spectators via this path.
    /// </para>
    /// </summary>
    IReadOnlyList<CardSnapshotDto>? LibraryView = null,
    /// <summary>
    /// CR 701.42 — peeked top-N of the surveilling player's library, in
    /// top-to-bottom order. Non-null only on surveil prompts; null on every
    /// other prompt kind. Privacy posture matches <see cref="LibraryView"/>:
    /// shipped per-recipient, never broadcast.
    /// </summary>
    IReadOnlyList<CardSnapshotDto>? SurveilView = null,
    /// <summary>
    /// CR 117.x / 605.1 — Yes/No prompt body (question + optional source
    /// card label + optional Yes/No button overrides). Non-null only on
    /// Yes/No prompts; null on every other prompt kind.
    /// <see cref="GameFacade.BuildPrompt"/> forwards this onto
    /// <see cref="PromptDto.YesNoView"/>.
    /// </summary>
    YesNoViewDto? YesNoView = null,
    /// <summary>
    /// CR 701.15 — reveal-and-choose prompt body (Malevolent Rumble,
    /// Impulse, Sleight of Hand, See the Unwritten, …). Non-null only on
    /// <c>chooseFromRevealed</c> prompts; null on every other prompt kind.
    /// <see cref="GameFacade.BuildPrompt"/> forwards this onto
    /// <see cref="PromptDto.RevealView"/>. Privacy posture matches
    /// <see cref="LibraryView"/>: shipped per-recipient, never broadcast
    /// to opponents or spectators (a reveal-and-choose still privately
    /// surfaces non-eligible cards back to the caster only — Malevolent
    /// Rumble's losers go to the graveyard publicly, but the prompt
    /// itself ships before the moves resolve so the opponent shouldn't
    /// see the candidates yet).
    /// </summary>
    RevealView? RevealView = null,
    /// <summary>
    /// CR 103.4 — number of cards the player must put on the bottom of their
    /// library after a London mulligan (equals the number of mulligans
    /// taken). Non-null only on <c>ChooseCardsToBottomCommand</c> prompts;
    /// null on every other prompt kind. <see cref="GameFacade.BuildPrompt"/>
    /// forwards this onto <see cref="PromptDto.BottomCount"/>.
    /// </summary>
    int? BottomCount = null);
