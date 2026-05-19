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
    // Cards the bot proposed to cast but that didn't actually leave hand
    // (no SpellDef match, target-fill failed, etc.). Cleared each turn —
    // if the bot picks something up next turn it might be castable then.
    private readonly HashSet<Guid> _failedThisTurn = new();
    private int _failedTurnNumber = -1;
    private Guid? _lastProposed;

    public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
    {
        // Reset failure memo on turn boundary.
        if (ctx.TurnNumber != _failedTurnNumber)
        {
            _failedThisTurn.Clear();
            _failedTurnNumber = ctx.TurnNumber;
            _lastProposed = null;
        }
        // If our previous proposal is still in hand, the dispatcher rotated
        // it on failure — mark it dead for this turn.
        if (_lastProposed is Guid prev
            && ctx.Self.Zones.Hand.GetCards().Any(c => c.InstanceId == prev))
        {
            _failedThisTurn.Add(prev);
        }
        _lastProposed = null;
        // Only attempt during a Main phase on our own turn with an empty
        // stack — land drops + sorcery-speed casts are illegal elsewhere.
        var phase = ctx.CurrentPhase;
        var sorceryWindow = phase == Majik.Core.StateMachine.PhaseStateType.Main
            && ReferenceEquals(ctx.Self, ctx.ActivePlayer)
            && ctx.Stack.IsEmpty;

        if (!sorceryWindow) return Task.FromResult(PriorityAction.Pass);

        // 1. Land drop, if we have one and haven't dropped this turn.
        var land = ctx.Self.Zones.Hand.GetCards()
            .FirstOrDefault(c => c.HasType(CardType.Land));
        if (land != null)
        {
            return Task.FromResult<PriorityAction>(new PriorityAction.PlayLand(land));
        }

        // 2. Highest-CMC affordable spell — permanents (resolve via vanilla
        //    SpellDefinition) plus instants/sorceries (caller's
        //    SpellDefinitionResolver may bind effects; if not, the dispatcher
        //    rotates the card on fail so we don't waste it).
        var hand = ctx.Self.Zones.Hand.GetCards();
        var candidates = hand
            .Where(c => !c.HasType(CardType.Land))
            .Where(IsCastableSpell)
            .Where(c => !_failedThisTurn.Contains(c.InstanceId))
            // Effective cost (CR 117.7 — Affinity / cost-reducers) so
            // discounted spells are correctly judged affordable.
            .Select(c => new { Card = c, Cost = Majik.Core.Costs.CostReduction.GetEffectiveCost(c, ctx.Self) })
            .OrderByDescending(x => x.Cost.TotalValue)
            .ToList();

        foreach (var cand in candidates)
        {
            if (TryPickManaSources(ctx.Self, cand.Cost) != null)
            {
                _lastProposed = cand.Card.InstanceId;
                return Task.FromResult<PriorityAction>(
                    new PriorityAction.CastSpell(cand.Card,
                        Array.Empty<object>()));
            }
        }

        return Task.FromResult(PriorityAction.Pass);
    }

    private static bool IsCastableSpell(ICard c) =>
        c.HasType(CardType.Creature)
        || c.HasType(CardType.Artifact)
        || c.HasType(CardType.Enchantment)
        || c.HasType(CardType.Planeswalker)
        || c.HasType(CardType.Instant)
        || c.HasType(CardType.Sorcery);

    /// <summary>Greedy pick of untapped mana sources to cover <paramref name="cost"/>.
    /// Returns null when the cost can't be paid from current untapped lands.
    /// Pure (doesn't tap anything) — engine's ManaPaymentResolver does the
    /// actual tapping once the payment commits.</summary>
    private static List<ICard>? TryPickManaSources(Player self, ManaCost cost)
    {
        var pool = self.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => !p.IsTapped)
            .Where(p => p.Abilities.OfType<IManaAbility>().Any())
            .ToList();

        var picked = new List<ICard>();
        var used = new HashSet<Permanent>();

        bool Produces(Permanent p, Func<ManaCost, int> selector)
        {
            var mana = p.Abilities.OfType<IManaAbility>().First().ManaGenerated;
            return selector(mana) > 0;
        }

        var quotas = new (Func<ManaCost, int> selector, int needed)[]
        {
            (m => m.White, cost.White),
            (m => m.Blue,  cost.Blue),
            (m => m.Black, cost.Black),
            (m => m.Red,   cost.Red),
            (m => m.Green, cost.Green),
        };

        foreach (var (selector, needed) in quotas)
        {
            for (var i = 0; i < needed; i++)
            {
                var src = pool.FirstOrDefault(p => !used.Contains(p) && Produces(p, selector));
                if (src == null) return null;
                used.Add(src);
                picked.Add(src);
            }
        }
        for (var i = 0; i < cost.Generic; i++)
        {
            var src = pool.FirstOrDefault(p => !used.Contains(p));
            if (src == null) return null;
            used.Add(src);
            picked.Add(src);
        }
        return picked;
    }

    public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
    {
        // Keep hands with 2–5 lands (CR 103.4 mulligan policy — most
        // 60-card decks want 2–4 lands in their opening seven). Below
        // 2 = mana-screwed; above 5 = mana-flooded. Always keep after
        // 3 mulligans to avoid digging into a one-card hand.
        var landCount = hand.Count(c => c.HasType(CardType.Land));
        var keep = mulligansTaken >= 3 || (landCount >= 2 && landCount <= 5);
        return Task.FromResult(keep ? MulliganDecision.Keep : MulliganDecision.Mulligan);
    }

    public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
    {
        if (request.LegalCandidates.Count > 0)
        {
            return Task.FromResult<IReadOnlyList<object>>(
                request.LegalCandidates.Take(request.MinTargets).ToList());
        }

        // Empty candidate list — fall back to engine-side picks. Card
        // binders that lack a candidate-gathering pass (e.g. damage-any
        // templates) get sensible defaults so the cast doesn't crash.
        var opponent = ctx.AllPlayers.FirstOrDefault(p => !ReferenceEquals(p, ctx.Self));
        var picked = new List<object>();
        var label = (request.Description ?? "").ToLowerInvariant();

        for (var i = 0; i < request.MinTargets; i++)
        {
            object? choice = label switch
            {
                _ when label.Contains("player") || label.Contains("any target")
                    => opponent,
                _ when label.Contains("creature")
                    => opponent?.Zones.Battlefield.GetCards()
                        .OfType<Creature>().FirstOrDefault(),
                _ when label.Contains("permanent")
                    => opponent?.Zones.Battlefield.GetCards().FirstOrDefault(),
                _ when label.Contains("spell")
                    => ctx.Stack.Top,
                _ => opponent,
            };
            if (choice != null) picked.Add(choice);
        }
        return Task.FromResult<IReadOnlyList<object>>(picked);
    }

    public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine.ToList());

    public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default)
    {
        var sources = TryPickManaSources(ctx.Self, cost) ?? new List<ICard>();
        return Task.FromResult(new ManaPayment(sources));
    }

    public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
    {
        var defender = ctx.AllPlayers.First(p => !ReferenceEquals(p, ctx.Self));
        var defenderCreatures = defender.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => !c.IsTapped).ToList();

        // Skip suicidal attacks: don't swing with a creature smaller than
        // every untapped opposing creature unless the defender's life is
        // dangerously low (lethal reach this turn).
        var totalAttackPower = eligibleAttackers.Sum(c => c.Power);
        var reach = totalAttackPower >= defender.LifeTotal;
        var attacks = new List<AttackerDeclaration>();
        foreach (var atk in eligibleAttackers)
        {
            var willDieFromAll = defenderCreatures.All(d => d.Power >= atk.Toughness)
                                 && defenderCreatures.Count > 0;
            if (willDieFromAll && !reach) continue;
            attacks.Add(new AttackerDeclaration(atk, defender));
        }
        return Task.FromResult(new CombatPlan(attacks));
    }

    public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
    {
        var assignments = new List<BlockerDeclaration>();
        var available = eligibleBlockers.ToList();

        // Defender's life vs incoming raw damage — chump-block only when
        // unblocked damage would otherwise be lethal.
        var incomingDamage = attackers.Sum(a => a.Power);
        var lethalIncoming = incomingDamage >= ctx.Self.LifeTotal;

        foreach (var atk in attackers)
        {
            // 1. Safe block that ALSO kills attacker — best outcome (one-sided).
            var safeKill = available
                .Where(b => b.Toughness > atk.Power && b.Power >= atk.Toughness)
                .OrderBy(b => b.Power) // preserve bigger blockers
                .FirstOrDefault();
            if (safeKill != null)
            {
                assignments.Add(new BlockerDeclaration(safeKill, atk));
                available.Remove(safeKill);
                continue;
            }

            // 2. Profitable trade — both die, attacker's CMC ≥ blocker's.
            var trade = available
                .Where(b => b.Power >= atk.Toughness && atk.Power >= b.Toughness)
                .Where(b => ManaCost.Parse(atk.ManaCost ?? "").TotalValue
                            >= ManaCost.Parse(b.ManaCost ?? "").TotalValue)
                .OrderBy(b => ManaCost.Parse(b.ManaCost ?? "").TotalValue)
                .FirstOrDefault();
            if (trade != null)
            {
                assignments.Add(new BlockerDeclaration(trade, atk));
                available.Remove(trade);
                continue;
            }

            // 3. Safe-but-doesn't-kill — smallest tough that survives.
            //    (Existing behaviour, preserved for test continuity.)
            var safe = available
                .Where(b => b.Toughness > atk.Power)
                .OrderBy(b => b.Toughness)
                .FirstOrDefault();
            if (safe != null)
            {
                assignments.Add(new BlockerDeclaration(safe, atk));
                available.Remove(safe);
                continue;
            }

            // 4. Chump — blocker dies, attacker lives. Only when otherwise
            //    lethal this combat step.
            if (lethalIncoming)
            {
                var chump = available
                    .OrderBy(b => ManaCost.Parse(b.ManaCost ?? "").TotalValue)
                    .FirstOrDefault();
                if (chump != null)
                {
                    assignments.Add(new BlockerDeclaration(chump, atk));
                    available.Remove(chump);
                    incomingDamage -= atk.Power;
                    lethalIncoming = incomingDamage >= ctx.Self.LifeTotal;
                    continue;
                }
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
