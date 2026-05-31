using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Game;

/// <summary>
/// CR 602 — activating an activated ability is symmetric to casting a
/// spell. The ability is announced, costs are paid, targets are chosen,
/// and the ability is put on the stack (mana abilities skip the stack
/// per CR 605 — those route through <see cref="ManaActivationFlow"/>
/// instead, MVP not yet split).
///
/// Phase 15 MVP focuses on the stack-using path:
///   1. agent supplies targets (skipped if none)
///   2. agent supplies mana payment
///   3. ability pushed onto stack; AbilityActivatedEvent fires
/// </summary>
public sealed class AbilityActivationFlow
{
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly IEventBus _bus;

    public AbilityActivationFlow(Majik.Core.Stack.Stack stack, IEventBus bus)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public async Task<IActivatedAbility> ActivateAsync(
        Player activator,
        IActivatedAbility ability,
        IReadOnlyList<TargetRequest> targetRequests,
        ManaCost? cost,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct = default)
    {
        if (activator == null) throw new ArgumentNullException(nameof(activator));
        if (ability == null) throw new ArgumentNullException(nameof(ability));
        if (agent == null) throw new ArgumentNullException(nameof(agent));

        // Targets (if any) — Rule 602.2b. Collect via the shared PLAN 01
        // (Slice E) pipeline and store on the ability so effects at resolution
        // time can read ability.ChosenTargets[n][0] etc. The ability path does
        // NOT gate on min cardinality (behaviour-preserving — the legacy inline
        // loop accepted under-filled picks).
        var collected = await Targeting.TargetCollection.CollectAsync(
            targetRequests ?? Array.Empty<TargetRequest>(),
            card: null,
            ctx,
            agent,
            throwOnInsufficient: false,
            ct);

        if (ability is ActivatedAbility aa)
        {
            aa.SetChosenTargets(collected);
        }

        // Mana payment (if cost supplied).
        if (cost != null && !cost.IsZero)
        {
            _ = await agent.ChooseManaSourcesAsync(ctx, cost, ct);
            activator.PayMana(cost);
        }

        _stack.Push(ability);
        _bus.Publish(new AbilityActivatedEvent(ability));
        return ability;
    }
}
