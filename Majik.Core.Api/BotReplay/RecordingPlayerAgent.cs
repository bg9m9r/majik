using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Api.BotReplay;

/// <summary>
/// Decorator over the live bot agent that durably records every answer at the
/// <see cref="IPlayerAgent"/> boundary before returning it verbatim. Installed
/// ONLY by the server layer (<c>ServerGameFactory.InstallBotAgent</c>) when
/// <c>EnginePersistenceOptions.Enabled</c> — sandbox/sim agents never see it
/// by construction.
///
/// <para>It overrides exactly the 15 primitive decision methods
/// <c>BotPlayerAgent</c> overrides (see <c>CodecCoverageTripwireTests</c>);
/// interface DEFAULT methods funnel into these primitives through
/// <c>this</c>-dispatch, so the whole prompt surface is covered.</para>
///
/// <para>The append is AWAITED before the answer is returned (ordering +
/// durability; the append is ~ms against 1500&#160;ms-class decisions). An
/// <see cref="UnsupportedBotDecisionException"/> from the encoder is a logged
/// degrade: the answer is returned unrecorded (the live game continues; a
/// later rehydrate stops gracefully at the gap) — never a corrupt record.</para>
/// </summary>
public sealed class RecordingPlayerAgent : IPlayerAgent
{
    private readonly IPlayerAgent _inner;
    private readonly Func<BotDecisionRecord, Task> _record;
    private readonly Action<UnsupportedBotDecisionException>? _onUnsupported;
    private int _botSeq;

    /// <param name="inner">The live bot agent every prompt is delegated to.</param>
    /// <param name="record">Durable append — awaited before each answer returns.</param>
    /// <param name="startSeq">First botSeq to stamp — 0 for a fresh match;
    /// <c>records.Count</c> when continuing after a rehydrated replay.</param>
    /// <param name="onUnsupported">Invoked when an answer cannot be encoded
    /// (exotic cost/candidate type). Null = rethrow (strict mode for tests);
    /// the server passes a logging callback so live play degrades instead.</param>
    public RecordingPlayerAgent(
        IPlayerAgent inner,
        Func<BotDecisionRecord, Task> record,
        int startSeq = 0,
        Action<UnsupportedBotDecisionException>? onUnsupported = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _record = record ?? throw new ArgumentNullException(nameof(record));
        _botSeq = startSeq;
        _onUnsupported = onUnsupported;
    }

    /// <summary>The wrapped live agent (used by tests / diagnostics).</summary>
    public IPlayerAgent Inner => _inner;

    private async Task<T> RecordAsync<T>(T answer, BotDecisionKind kind, Func<T, BotDecisionPayload> encode)
    {
        BotDecisionPayload payload;
        try
        {
            payload = encode(answer);
        }
        catch (UnsupportedBotDecisionException ex) when (_onUnsupported != null)
        {
            // Logged degrade: skip the record (botSeq does not advance), keep
            // the live game running. The decision stream ends usable up to
            // here; a later rehydrate stops gracefully at the gap.
            _onUnsupported(ex);
            return answer;
        }

        await _record(new BotDecisionRecord(_botSeq, kind, payload)).ConfigureAwait(false);
        _botSeq++;
        return answer;
    }

    public async Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChoosePriorityActionAsync(ctx, ct).ConfigureAwait(false),
            BotDecisionKind.Priority, BotDecisionCodec.EncodePriority).ConfigureAwait(false);

    public async Task<MulliganDecision> ChooseMulliganAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct).ConfigureAwait(false),
            BotDecisionKind.Mulligan, BotDecisionCodec.EncodeMulligan).ConfigureAwait(false);

    public async Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct).ConfigureAwait(false),
            BotDecisionKind.CardsToBottom, BotDecisionCodec.EncodeCardsToBottom).ConfigureAwait(false);

    public async Task<IReadOnlyList<object>> ChooseTargetsAsync(
        GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseTargetsAsync(ctx, request, ct).ConfigureAwait(false),
            BotDecisionKind.Targets, BotDecisionCodec.EncodeTargets).ConfigureAwait(false);

    public async Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseXAsync(ctx, source, ct).ConfigureAwait(false),
            BotDecisionKind.X, BotDecisionCodec.EncodeX).ConfigureAwait(false);

    public async Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseModeAsync(ctx, modes, modeIntents, ct).ConfigureAwait(false),
            BotDecisionKind.Mode, BotDecisionCodec.EncodeMode).ConfigureAwait(false);

    public async Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
        GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.OrderTriggersAsync(ctx, mine, ct).ConfigureAwait(false),
            BotDecisionKind.TriggerOrder, BotDecisionCodec.EncodeTriggerOrder).ConfigureAwait(false);

    public async Task<ManaPayment> ChooseManaSourcesAsync(
        GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseManaSourcesAsync(ctx, cost, ct).ConfigureAwait(false),
            BotDecisionKind.ManaSources, BotDecisionCodec.EncodeManaSources).ConfigureAwait(false);

    public async Task<CombatPlan> DeclareAttackersAsync(
        GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.DeclareAttackersAsync(ctx, eligibleAttackers, ct).ConfigureAwait(false),
            BotDecisionKind.Attackers, BotDecisionCodec.EncodeAttackers).ConfigureAwait(false);

    public async Task<BlockPlan> DeclareBlockersAsync(
        GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers,
        CancellationToken ct = default)
        => await RecordAsync(
            await _inner.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct).ConfigureAwait(false),
            BotDecisionKind.Blockers, BotDecisionCodec.EncodeBlockers).ConfigureAwait(false);

    public async Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseScryDecisionAsync(ctx, peeked, ct).ConfigureAwait(false),
            BotDecisionKind.Scry, BotDecisionCodec.EncodeScry).ConfigureAwait(false);

    public async Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseSurveilDecisionAsync(ctx, peeked, ct).ConfigureAwait(false),
            BotDecisionKind.Surveil, BotDecisionCodec.EncodeSurveil).ConfigureAwait(false);

    public async Task<ICard?> ChooseLibraryPickAsync(
        GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseLibraryPickAsync(ctx, candidates, kindLabel, ct).ConfigureAwait(false),
            BotDecisionKind.LibraryPick, BotDecisionCodec.EncodeLibraryPick).ConfigureAwait(false);

    public async Task<bool> ChooseYesNoAsync(
        GameContext? ctx, string question, string? sourceCardName, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseYesNoAsync(ctx, question, sourceCardName, ct).ConfigureAwait(false),
            BotDecisionKind.YesNo, BotDecisionCodec.EncodeYesNo).ConfigureAwait(false);

    public async Task<IReadOnlyList<object>> ChooseAsync(
        GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        => await RecordAsync(
            await _inner.ChooseAsync(ctx, req, ct).ConfigureAwait(false),
            BotDecisionKind.Choose, BotDecisionCodec.EncodeChoose).ConfigureAwait(false);
}
