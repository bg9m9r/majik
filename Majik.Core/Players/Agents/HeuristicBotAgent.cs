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

        // 2. Cheapest castable permanent (creature / artifact / enchantment /
        //    planeswalker). Instants and sorceries deferred — they need
        //    SpellDefinition lookup which the bot can't do without a
        //    binder. Permanents resolve fine with a vanilla SpellDefinition.
        var hand = ctx.Self.Zones.Hand.GetCards();
        var candidates = hand
            .Where(c => !c.HasType(CardType.Land))
            .Where(IsPermanentSpell)
            .Select(c => new { Card = c, Cost = ManaCost.Parse(c.ManaCost ?? "") })
            .OrderBy(x => x.Cost.TotalValue)
            .ToList();

        foreach (var cand in candidates)
        {
            if (TryPickManaSources(ctx.Self, cand.Cost) != null)
            {
                return Task.FromResult<PriorityAction>(
                    new PriorityAction.CastSpell(cand.Card,
                        Array.Empty<object>()));
            }
        }

        return Task.FromResult(PriorityAction.Pass);
    }

    private static bool IsPermanentSpell(ICard c) =>
        c.HasType(CardType.Creature)
        || c.HasType(CardType.Artifact)
        || c.HasType(CardType.Enchantment)
        || c.HasType(CardType.Planeswalker);

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
    {
        var sources = TryPickManaSources(ctx.Self, cost) ?? new List<ICard>();
        return Task.FromResult(new ManaPayment(sources));
    }

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
