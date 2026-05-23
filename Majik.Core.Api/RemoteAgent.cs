using Majik.Core.Abilities;
using Majik.Core.Api.Commands;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
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
        _pending = null;
        _pendingKinds = null;
        Resolve(pending, command);
    }

    private void Resolve(object tcs, GameCommand command)
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
            case OrderTriggersCommand:
                throw new NotImplementedException("OrderTriggers resolution wired in P9.7.");
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
        => Prompt<PriorityAction>(ct,
            typeof(PassPriorityCommand), typeof(PlayLandCommand), typeof(CastSpellCommand));

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
        => throw new NotImplementedException("OrderTriggers wired in P9.7.");

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
