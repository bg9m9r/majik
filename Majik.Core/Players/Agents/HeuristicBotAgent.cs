using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Phase 27 heuristic bot. Smarter than <see cref="DeterministicBotAgent"/>:
///
///   - Priority: if a land is in hand and a land drop is legal, plays it;
///     otherwise passes. (Spell-casting decision deferred — needs cost
///     evaluator + target selection, see remaining Phase 15.)
///   - Combat (attack): declares every non-sick untapped creature as an
///     attacker, swinging at the defender.
///   - Combat (block): for each attacker, blocks with the smallest creature
///     whose toughness strictly exceeds the attacker's power (a "safe"
///     block that doesn't lose the blocker). If no safe blocker exists,
///     doesn't block that attacker.
///
/// Everything else delegates to the default no-op behaviour from
/// <see cref="DeterministicBotAgent"/>.
/// </summary>
public sealed class HeuristicBotAgent : IPlayerAgent
{
    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
    {
        // Only attempt during a Main phase on our own turn — land drops
        // are illegal anywhere else and would trip the LandDropTracker.
        var phase = ctx.CurrentPhase;
        if (phase == Majik.Core.StateMachine.PhaseStateType.Main
            && ReferenceEquals(ctx.Self, ctx.ActivePlayer)
            && ctx.Stack.IsEmpty)
        {
            var land = ctx.Self.Zones.Hand.GetCards()
                .FirstOrDefault(c => c.HasType(CardType.Land));
            if (land != null)
            {
                return Task.FromResult<PriorityAction>(new PriorityAction.PlayLand(land));
            }
        }

        return Task.FromResult(PriorityAction.Pass);
    }

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
        => Task.FromResult(MulliganDecision.Keep);

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<object>>(request.LegalCandidates.Take(request.MinTargets).ToList());

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine.ToList());

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
        => Task.FromResult(ManaPayment.Empty);

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
    {
        var defender = ctx.AllPlayers.First(p => !ReferenceEquals(p, ctx.Self));
        var attacks = eligibleAttackers
            .Select(c => new AttackerDeclaration(c, defender))
            .ToList();
        return Task.FromResult(new CombatPlan(attacks));
    }

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
    {
        var assignments = new List<BlockerDeclaration>();
        var available = eligibleBlockers.ToList();

        foreach (var atk in attackers)
        {
            // Find smallest blocker whose toughness > attacker power (won't die).
            var safe = available
                .Where(b => b.Toughness > atk.Power)
                .OrderBy(b => b.Toughness)
                .FirstOrDefault();
            if (safe != null)
            {
                assignments.Add(new BlockerDeclaration(safe, atk));
                available.Remove(safe);
            }
        }

        return Task.FromResult(new BlockPlan(assignments));
    }

    public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
        GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
    {
        // Heuristic: bottom the most expensive cards first.
        var sorted = hand.OrderByDescending(c =>
                Majik.Core.ValueObjects.ManaCost.Parse(c.ManaCost ?? "").TotalValue)
            .Take(countToBottom).ToList();
        return Task.FromResult<IReadOnlyList<ICard>>(sorted);
    }
}
