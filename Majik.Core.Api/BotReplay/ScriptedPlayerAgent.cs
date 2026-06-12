using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Api.BotReplay;

/// <summary>
/// Replay-side bot agent: answers every bot prompt by dequeuing the next
/// <see cref="BotDecisionRecord"/> from the match's recorded stream, asserting
/// the kind matches, and decoding the Id-level payload against the rebuilt
/// facade's live objects. NOTHING is recomputed — wall-clock-nondeterministic
/// search (MCTS) replays its original answers verbatim.
///
/// <para>A kind mismatch or a decode miss throws
/// <see cref="InvalidOperationException"/>, which surfaces as the replay's
/// existing graceful stop (rehydrate fails; the match is lost, never wedged).</para>
///
/// <para><b>Live-edge handoff:</b> once the script is exhausted the agent
/// falls through to <paramref name="continuation"/> — the fresh
/// <see cref="RecordingPlayerAgent"/>-wrapped live bot, whose
/// <c>startSeq</c> continues the stream at <c>script.Count</c>. The handoff
/// is composed in (not swapped in after replay) because
/// <c>GameFacade</c> agents cannot be replaced once the game has started,
/// and the rebuilt game may prompt the bot again before (or the instant)
/// replay returns. With no continuation, exhaustion throws — the strict
/// posture for pure-replay scenarios.</para>
/// </summary>
public sealed class ScriptedPlayerAgent : IPlayerAgent
{
    private readonly IReadOnlyList<BotDecisionRecord> _script;
    private readonly IPlayerAgent? _continuation;
    private int _next;

    public ScriptedPlayerAgent(
        IReadOnlyList<BotDecisionRecord> script,
        IPlayerAgent? continuation = null)
    {
        _script = script ?? throw new ArgumentNullException(nameof(script));
        _continuation = continuation;
    }

    /// <summary>How many recorded decisions have been consumed so far.</summary>
    public int Consumed => _next;

    /// <summary>The live-edge fall-through agent (typically a
    /// <see cref="RecordingPlayerAgent"/> over the live bot), or null in the
    /// strict pure-replay posture. Exposed for installation-seam tests.</summary>
    public IPlayerAgent? Continuation => _continuation;

    /// <summary>True once every recorded decision has been replayed.</summary>
    public bool Exhausted => _next >= _script.Count;

    /// <summary>
    /// Dequeue the next record for <paramref name="kind"/>, or null when the
    /// script is exhausted AND a continuation exists (live-edge fall-through).
    /// </summary>
    private BotDecisionPayload? Next(BotDecisionKind kind)
    {
        if (_next >= _script.Count)
        {
            if (_continuation != null) return null;
            throw new InvalidOperationException(
                $"Bot-decision stream exhausted at botSeq {_next} but the replay raised " +
                $"another bot prompt ({kind}) — recorded stream is incomplete (desync).");
        }

        var record = _script[_next];
        if (record.Kind != kind)
        {
            throw new InvalidOperationException(
                $"Bot-decision stream desync at botSeq {record.BotSeq}: recorded kind " +
                $"'{record.Kind}' but the replay prompt asks for '{kind}'.");
        }

        _next++;
        return record.Payload;
    }

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => Next(BotDecisionKind.Priority) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodePriority(p, ctx, ctx.Self))
            : _continuation!.ChoosePriorityActionAsync(ctx, ct);

    public Task<MulliganDecision> ChooseMulliganAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => Next(BotDecisionKind.Mulligan) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeMulligan(p))
            : _continuation!.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => Next(BotDecisionKind.CardsToBottom) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeCardsToBottom(p, hand))
            : _continuation!.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(
        GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => Next(BotDecisionKind.Targets) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeTargets(p, ctx))
            : _continuation!.ChooseTargetsAsync(ctx, request, ct);

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => Next(BotDecisionKind.X) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeX(p))
            : _continuation!.ChooseXAsync(ctx, source, ct);

    public Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default)
        => Next(BotDecisionKind.Mode) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeMode(p))
            : _continuation!.ChooseModeAsync(ctx, modes, modeIntents, ct);

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
        GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => Next(BotDecisionKind.TriggerOrder) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeTriggerOrder(p, mine))
            : _continuation!.OrderTriggersAsync(ctx, mine, ct);

    public Task<ManaPayment> ChooseManaSourcesAsync(
        GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => Next(BotDecisionKind.ManaSources) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeManaSources(p, ctx))
            : _continuation!.ChooseManaSourcesAsync(ctx, cost, ct);

    public Task<CombatPlan> DeclareAttackersAsync(
        GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => Next(BotDecisionKind.Attackers) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeAttackers(p, ctx, eligibleAttackers))
            : _continuation!.DeclareAttackersAsync(ctx, eligibleAttackers, ct);

    public Task<BlockPlan> DeclareBlockersAsync(
        GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers,
        CancellationToken ct = default)
        => Next(BotDecisionKind.Blockers) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeBlockers(p, attackers, eligibleBlockers))
            : _continuation!.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct);

    public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => Next(BotDecisionKind.Scry) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeScry(p, peeked))
            : _continuation!.ChooseScryDecisionAsync(ctx, peeked, ct);

    public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => Next(BotDecisionKind.Surveil) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeSurveil(p, peeked))
            : _continuation!.ChooseSurveilDecisionAsync(ctx, peeked, ct);

    public Task<ICard?> ChooseLibraryPickAsync(
        GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel, CancellationToken ct = default)
        => Next(BotDecisionKind.LibraryPick) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeLibraryPick(p, candidates))
            : _continuation!.ChooseLibraryPickAsync(ctx, candidates, kindLabel, ct);

    public Task<bool> ChooseYesNoAsync(
        GameContext? ctx, string question, string? sourceCardName, CancellationToken ct = default)
        => Next(BotDecisionKind.YesNo) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeYesNo(p))
            : _continuation!.ChooseYesNoAsync(ctx, question, sourceCardName, ct);

    public Task<IReadOnlyList<object>> ChooseAsync(
        GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        => Next(BotDecisionKind.Choose) is { } p
            ? Task.FromResult(BotDecisionCodec.DecodeChoose(p, req, ctx))
            : _continuation!.ChooseAsync(ctx, req, ct);
}
