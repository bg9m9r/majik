using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
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

    public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, CancellationToken ct = default)
        => Task.FromResult(Pop(_modes, "mode"));

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => Task.FromResult(Pop(_triggerOrders, "trigger order"));

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => Task.FromResult(Pop(_manaPayments, "mana"));

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
        => Task.FromResult(Pop(_attackPlans, "attackers"));

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
        => Task.FromResult(Pop(_blockPlans, "blockers"));

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
