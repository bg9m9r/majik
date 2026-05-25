using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.ValueObjects;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Test helper. Pre-queue responses for each choice category; the agent
/// returns them FIFO. Throws if asked for a choice nothing was queued for —
/// a missing script entry is almost always a test bug, not a feature.
/// </summary>
public sealed class ScriptedAgent : IPlayerAgent
{
    private readonly Queue<PriorityAction> _priorityActions = new();
    private readonly Queue<MulliganDecision> _mulligans = new();
    private readonly Queue<IReadOnlyList<object>> _targets = new();
    private readonly Queue<int> _xValues = new();
    private readonly Queue<int> _modes = new();
    private readonly Queue<IReadOnlyList<ITriggeredAbility>> _triggerOrders = new();
    private readonly Queue<ManaPayment> _manaPayments = new();
    private readonly Queue<CombatPlan> _attackPlans = new();
    private readonly Queue<BlockPlan> _blockPlans = new();
    private readonly Queue<Func<IReadOnlyList<ICard>, IReadOnlyList<ICard>>> _bottomChoices = new();
    private readonly Queue<ScryAction.ScryDecision> _scryDecisions = new();
    private readonly Queue<SurveilAction.SurveilDecision> _surveilDecisions = new();
    private readonly Queue<Func<IReadOnlyList<Player>, Player?>> _giftRecipients = new();
    private readonly Queue<bool> _yesNoAnswers = new();
    private readonly Queue<Func<IReadOnlyList<ICard>, ICard?>> _fromHandChoices = new();
    private readonly Queue<Func<IReadOnlyList<ICard>, ICard?>> _fromBattlefieldChoices = new();

    public void QueuePriority(PriorityAction a) => _priorityActions.Enqueue(a);
    public void QueueMulligan(MulliganDecision d) => _mulligans.Enqueue(d);
    public void QueueCardsToBottom(Func<IReadOnlyList<ICard>, IReadOnlyList<ICard>> chooser)
        => _bottomChoices.Enqueue(chooser);
    public void QueueTargets(IReadOnlyList<object> targets) => _targets.Enqueue(targets);
    public void QueueX(int x) => _xValues.Enqueue(x);
    public void QueueMode(int m) => _modes.Enqueue(m);
    public void QueueTriggerOrder(IReadOnlyList<ITriggeredAbility> order) => _triggerOrders.Enqueue(order);
    public void QueueMana(ManaPayment p) => _manaPayments.Enqueue(p);
    public void QueueAttackers(CombatPlan p) => _attackPlans.Enqueue(p);
    public void QueueBlockers(BlockPlan p) => _blockPlans.Enqueue(p);
    /// <summary>Pre-queue a Scry decision; falls back to all-bottom when queue is empty.</summary>
    public void QueueScryDecision(ScryAction.ScryDecision d) => _scryDecisions.Enqueue(d);
    /// <summary>Pre-queue a Surveil decision; falls back to all-graveyard when queue is empty.</summary>
    public void QueueSurveilDecision(SurveilAction.SurveilDecision d) => _surveilDecisions.Enqueue(d);
    /// <summary>Pre-queue a Bloomburrow Gift recipient picker (CR 701.59); receives the live opponent
    /// pool and returns the chosen recipient or <c>null</c> to decline. Falls back to decline when
    /// the queue is empty (matches the legacy <see cref="IPlayerAgent"/> default).</summary>
    public void QueueGiftRecipient(Func<IReadOnlyList<Player>, Player?> chooser) => _giftRecipients.Enqueue(chooser);
    /// <summary>Convenience: pre-queue a single fixed gift recipient (or decline-null).</summary>
    public void QueueGiftRecipient(Player? recipient) => _giftRecipients.Enqueue(_ => recipient);
    /// <summary>Pre-queue a Yes/No answer for the next
    /// <see cref="IPlayerAgent.ChooseYesNoAsync"/> call. Throws if the
    /// queue is empty — a missing entry is almost always a test bug.</summary>
    public void QueueYesNo(bool answer) => _yesNoAnswers.Enqueue(answer);
    /// <summary>Pre-queue a hand-pick chooser for the next
    /// <see cref="IPlayerAgent.ChooseFromHandAsync"/> call; the chooser
    /// receives the live candidate list and returns the picked card or
    /// <c>null</c> to decline. Falls back to deterministic first-pick
    /// when no chooser is queued (matches the default interface
    /// implementation).</summary>
    public void QueueFromHand(Func<IReadOnlyList<ICard>, ICard?> chooser) => _fromHandChoices.Enqueue(chooser);
    /// <summary>Convenience: pre-queue a single fixed hand pick (or decline-null).</summary>
    public void QueueFromHand(ICard? pick) => _fromHandChoices.Enqueue(_ => pick);
    /// <summary>Pre-queue a battlefield-pick chooser for the next
    /// <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> call; the
    /// chooser receives the live candidate list and returns the picked
    /// permanent or <c>null</c> to decline. Falls back to deterministic
    /// first-pick when no chooser is queued (matches the default
    /// interface implementation).</summary>
    public void QueueFromBattlefield(Func<IReadOnlyList<ICard>, ICard?> chooser) => _fromBattlefieldChoices.Enqueue(chooser);
    /// <summary>Convenience: pre-queue a single fixed battlefield pick (or decline-null).</summary>
    public void QueueFromBattlefield(ICard? pick) => _fromBattlefieldChoices.Enqueue(_ => pick);

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
        => Task.FromResult(Pop(_priorityActions, "priority"));

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => Task.FromResult(Pop(_mulligans, "mulligan"));

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
    {
        if (countToBottom == 0) return Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        var chooser = Pop(_bottomChoices, "bottom cards");
        return Task.FromResult(chooser(hand));
    }

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => Task.FromResult(Pop(_targets, "targets"));

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => Task.FromResult(Pop(_xValues, "X"));

    public Task<int> ChooseModeAsync(
        GameContext ctx,
        IReadOnlyList<string> modes,
        IReadOnlyList<BotIntent>? modeIntents = null,
        CancellationToken ct = default)
        => Task.FromResult(Pop(_modes, "mode"));

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => Task.FromResult(Pop(_triggerOrders, "trigger order"));

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => Task.FromResult(Pop(_manaPayments, "mana"));

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => Task.FromResult(Pop(_attackPlans, "attackers"));

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        => Task.FromResult(Pop(_blockPlans, "blockers"));

    public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
    {
        if (_scryDecisions.Count > 0)
            return Task.FromResult(_scryDecisions.Dequeue());
        // Default: all peeked cards to bottom (matching pre-agent behaviour).
        return Task.FromResult(new ScryAction.ScryDecision(
            ToBottom: peeked.ToList(),
            TopOrder: Array.Empty<ICard>()));
    }

    public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
        GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
    {
        if (_surveilDecisions.Count > 0)
            return Task.FromResult(_surveilDecisions.Dequeue());
        // Default: all peeked cards to graveyard (matching pre-agent behaviour).
        return Task.FromResult(new SurveilAction.SurveilDecision(
            ToGraveyard: peeked.ToList(),
            TopOrder: Array.Empty<ICard>()));
    }

    public Task<ICard?> ChooseLibraryPickAsync(
        GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel, CancellationToken ct = default)
        => Task.FromResult<ICard?>(candidates.Count > 0 ? candidates[0] : null);

    public Task<bool> ChooseYesNoAsync(
        string question, BotIntent intent, CancellationToken ct = default)
        => Task.FromResult(Pop(_yesNoAnswers, "yes/no"));

    public Task<ICard?> ChooseFromHandAsync(
        Player chooser, IReadOnlyList<ICard> candidates, BotIntent intent, CancellationToken ct = default)
    {
        if (_fromHandChoices.Count == 0)
        {
            // No script entry queued — fall back to the deterministic
            // default (first candidate, or null when empty). Matches the
            // IPlayerAgent default and the pre-agent behaviour every
            // retrofitted factory used.
            return Task.FromResult<ICard?>(candidates.Count > 0 ? candidates[0] : null);
        }
        var chooserFn = _fromHandChoices.Dequeue();
        return Task.FromResult(chooserFn(candidates));
    }

    public Task<ICard?> ChooseFromBattlefieldAsync(
        Player chooser, IReadOnlyList<ICard> candidates, BotIntent intent, CancellationToken ct = default)
    {
        if (_fromBattlefieldChoices.Count == 0)
        {
            // No script entry queued — fall back to the deterministic
            // default (first candidate, or null when empty). Matches the
            // IPlayerAgent default.
            return Task.FromResult<ICard?>(candidates.Count > 0 ? candidates[0] : null);
        }
        var chooserFn = _fromBattlefieldChoices.Dequeue();
        return Task.FromResult(chooserFn(candidates));
    }

    public Task<Player?> ChooseGiftRecipientAsync(
        GameContext ctx, ICard source, string giftDescription,
        IReadOnlyList<Player> opponents, CancellationToken ct = default)
    {
        if (_giftRecipients.Count == 0)
        {
            // Decline by default — matches the IPlayerAgent default.
            return Task.FromResult<Player?>(null);
        }
        var chooser = _giftRecipients.Dequeue();
        return Task.FromResult(chooser(opponents));
    }

    private static T Pop<T>(Queue<T> q, string what)
    {
        if (q.Count == 0)
        {
            throw new InvalidOperationException(
                $"ScriptedAgent asked for {what} but no response was queued.");
        }

        return q.Dequeue();
    }
}
