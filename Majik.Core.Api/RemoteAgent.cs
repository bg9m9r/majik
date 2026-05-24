using Majik.Core.Abilities;
using Majik.Core.Api.Commands;
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
        _pending = null;
        _pendingKinds = null;
        _pendingTriggerOrder = null;
        Resolve(pending, command, triggerOrder);
    }

    private void Resolve(object tcs, GameCommand command, IReadOnlyList<ITriggeredAbility>? triggerOrder)
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
        => Prompt<PriorityAction>(ct, BuildPriorityKinds(ctx));

    /// <summary>
    /// Narrows the priority-prompt command kinds to those that are at least
    /// plausibly legal at this priority moment. The portal uses an exact
    /// match on <c>[PassPriorityCommand]</c> to auto-pass dead windows
    /// without bothering the user; sending the full 3-kind menu every time
    /// (the old behaviour) defeats that gate.
    ///
    /// Conservative by design — false positives (offering a kind the player
    /// can't actually use) are acceptable because the engine still
    /// validates each submitted command; false negatives (hiding a kind
    /// the player legitimately can use) would lock the user out and are
    /// catastrophic. The bot path (BotPlayerAgent / Heuristic / Deterministic)
    /// enumerates its own moves and does not consult ExpectedCommandKinds,
    /// so this narrowing is purely a UX hint for remote (human) clients.
    /// </summary>
    private static Type[] BuildPriorityKinds(GameContext ctx)
    {
        // PassPriorityCommand is always legal — passing priority is a
        // player's fundamental action at every priority window (CR 117.4).
        var kinds = new List<Type>(3) { typeof(PassPriorityCommand) };

        var hand = ctx.Self.Zones.Hand.GetCards();
        var sorceryWindow = ctx.CurrentPhase == PhaseStateType.Main
            && ReferenceEquals(ctx.Self, ctx.ActivePlayer)
            && ctx.Stack.IsEmpty;

        // CR 305.2 — lands are sorcery-speed-only, your-turn-only, and
        // stack-must-be-empty. We don't have a reference to LandDropTracker
        // here so we can't check the per-turn cap — overinclude when the
        // window is right and there's a land in hand; the engine's
        // LandDropTracker rejects an over-cap submission cleanly.
        if (sorceryWindow && hand.Any(c => c.HasType(CardType.Land)))
        {
            kinds.Add(typeof(PlayLandCommand));
        }

        // CR 302.1 / 307.1 / 117.1a — spells need either sorcery speed
        // (own main + empty stack) for vanilla cards or instant speed
        // (Instant card type or Flash keyword) anytime. Skip the
        // mana-source check entirely: it's expensive and the user might
        // legitimately want to float mana / activate a ritual first.
        // Including CastSpellCommand whenever there's at least one card
        // they could plausibly cast (now or after producing mana) keeps
        // the gate honest without starving the user of action.
        var hasCastable = hand.Any(c =>
            !c.HasType(CardType.Land)
            && (sorceryWindow || IsInstantSpeed(c)));
        if (hasCastable)
        {
            kinds.Add(typeof(CastSpellCommand));
        }

        // CR 605.1a / 605.3a — mana abilities are activated whenever the
        // controller has priority. Advertise the command kind whenever the
        // player controls at least one permanent with a mana ability; the
        // engine validates legality (untapped, CanActivate, etc.) on submit.
        var battlefield = ctx.Self.Zones.Battlefield.GetCards();
        var hasManaSource = battlefield.Any(c =>
            c.Abilities.OfType<Majik.Core.Abilities.IManaAbility>().Any());
        if (hasManaSource)
        {
            kinds.Add(typeof(ActivateManaAbilityCommand));
        }

        return kinds.ToArray();
    }

    /// <summary>Card is castable at instant speed: Instant card type, or
    /// any card with the Flash keyword (CR 702.8). Used by
    /// <see cref="BuildPriorityKinds"/> to decide whether
    /// <see cref="CastSpellCommand"/> belongs in the prompt kinds outside
    /// a sorcery window.</summary>
    private static bool IsInstantSpeed(ICard card)
    {
        if (card.HasType(CardType.Instant)) return true;
        return card.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, "Flash", StringComparison.OrdinalIgnoreCase));
    }

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => Prompt<MulliganDecision>(ct, typeof(MulliganCommand));

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => Prompt<IReadOnlyList<ICard>>(ct, typeof(ChooseCardsToBottomCommand));

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
        => Prompt<ManaPayment>(ct, typeof(ChooseManaCommand));

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

    // TODO (v2): wire Scry/Surveil prompts through the command channel
    // (ChooseScryCommand / ChooseSurveilCommand) once the prompt system
    // is updated to handle sync-over-async in effect closures.
    public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => throw new NotImplementedException("Scry prompt wired in v2 (effect async refactor).");

    public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => throw new NotImplementedException("Surveil prompt wired in v2 (effect async refactor).");

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
