using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// Orchestrates Rule 601 spell-casting steps via async agent prompts:
///   0. casting permission check (CR 117.1, sorcery vs instant speed)
///   1. announce spell; pay any additional costs (CR 601.2f)
///   2. choose modes
///   3. choose X (variable costs); X is added to the mana cost
///   4. choose targets
///   5. choose mana payment (alternative cost replaces printed cost; CR 118.9)
///   6. move card to stack, build Spell, push, publish SpellCastEvent
///   7. when spell resolves, alternative cost's OnResolved fires (e.g. exile)
/// </summary>
public sealed class SpellCastFlow
{
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zoneService;
    private readonly IEventBus _eventBus;

    public SpellCastFlow(
        Majik.Core.Stack.Stack stack,
        ZoneService zoneService,
        IEventBus eventBus)
    {
        _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        _zoneService = zoneService ?? throw new ArgumentNullException(nameof(zoneService));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async Task<Spells.Spell> CastAsync(
        Player caster,
        ICard card,
        SpellDefinition definition,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct = default,
        IReadOnlyList<IAdditionalCost>? additionalCosts = null,
        IAlternativeCost? alternativeCost = null,
        Majik.Core.Players.Agents.ManaPayment? preChosenMana = null,
        DelveCost? delveCost = null)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (agent == null) throw new ArgumentNullException(nameof(agent));

        // CR 117.1 — sorcery-speed gating (skipped when the alternative cost
        // specifies its own casting permission, e.g. Flashback from graveyard).
        if (alternativeCost == null
            && ctx.CurrentPhase.HasValue
            && !CastingPermission.CanCast(card, caster, ctx.ActivePlayer,
                ctx.CurrentPhase.Value, _stack.IsEmpty, out var reason))
        {
            throw new InvalidOperationException($"Cannot cast {card.Name}: {reason}");
        }

        // Alternative cost legality check (CR 118.9 — zone restriction etc.).
        if (alternativeCost != null && !alternativeCost.CanCastFor(card, caster))
        {
            throw new InvalidOperationException(
                $"Cannot use alternative cost {alternativeCost.Description} for {card.Name}");
        }

        // CR 118.9 — Pitch alt-cost imposes an additional context check:
        // "If it's not your turn …". Force-of-Will-cycle spells embed this
        // timing predicate in the alt cost itself. Other alt-costs (Flashback,
        // Spectacle, Evoke, …) carry their own zone / state predicates inside
        // CanCastFor and don't need this hook. Keep the surface minimal —
        // SpellCastFlow stays generic, only this one concrete type gets the
        // activePlayer gate.
        if (alternativeCost is PitchAlternativeCost pitch
            && !pitch.IsLegalInContext(ctx.ActivePlayer))
        {
            throw new InvalidOperationException(
                $"Cannot cast {card.Name} via pitch: it is the caster's own turn (CR 118.9 timing gate).");
        }

        // CR 601.2f — additional costs first, before mana payment.
        // Merge the caller-supplied list with any costs the card itself
        // declares via SpellDefinition.AdditionalCosts (template-bound
        // "As an additional cost to cast this spell, sacrifice …" cards).
        var mergedAdditional = new List<IAdditionalCost>();
        if (definition.AdditionalCosts is { Count: > 0 } defCosts)
        {
            mergedAdditional.AddRange(defCosts);
        }
        if (additionalCosts != null)
        {
            mergedAdditional.AddRange(additionalCosts);
        }

        // Pre-check legality so we fail BEFORE mutating any zone — CR
        // 601.2g requires that if any cost can't be paid the cast is
        // illegal and the game is rewound. v1 short-circuit: if any cost
        // refuses, throw, no partial payment.
        foreach (var pre in mergedAdditional)
        {
            if (!pre.CanPay(caster))
            {
                throw new InvalidOperationException(
                    $"Cannot pay additional cost: {pre.Description}");
            }
        }

        foreach (var addCost in mergedAdditional)
        {
            if (!addCost.Pay(caster))
            {
                throw new InvalidOperationException(
                    $"Failed to pay additional cost: {addCost.Description}");
            }
        }

        int? mode = null;
        if (definition.Modes.Count > 0)
        {
            mode = await agent.ChooseModeAsync(ctx, definition.Modes, definition.ModeIntents, ct);
        }

        int? xValue = null;
        if (definition.HasVariableX)
        {
            xValue = await agent.ChooseXAsync(ctx, card, ct);
        }

        var collectedTargets = new List<IReadOnlyList<object>>(definition.TargetRequests.Count);
        foreach (var req in definition.TargetRequests)
        {
            var picked = await agent.ChooseTargetsAsync(ctx, req, ct);
            // CR 601.2c — cast is illegal if the agent can't pick enough
            // legal targets. Throw a typed exception so the caller (cast
            // dispatcher) can catch and abort cleanly instead of letting
            // EffectFactory crash on Targets[0][0].
            if (picked.Count < req.MinTargets)
            {
                throw new InvalidOperationException(
                    $"Cannot cast {card.Name}: target request '{req.Description}' " +
                    $"needs {req.MinTargets}, agent provided {picked.Count}.");
            }
            collectedTargets.Add(picked);
        }

        // Cost — printed + X, OR alternative cost when supplied. CR 117.7:
        // also subtract any CostReductionAbility on the card (Affinity etc.).
        var totalCost = alternativeCost?.AlternativeManaCost
            ?? Majik.Core.Costs.CostReduction.GetEffectiveCost(card, caster);
        if (xValue.HasValue && xValue.Value > 0)
        {
            totalCost = totalCost.AddGenericCost(xValue.Value);
        }

        // CR 702.66 — Delve. Each exiled graveyard card reduces the
        // spell's total generic mana by 1. Apply after X (X is generic
        // and is delve-payable per CR 702.66 + CR 601.2g order) and
        // after cost reduction. Pay the exile portion of the cost now —
        // CR 702.66b says delve is paid when the spell is cast.
        if (delveCost != null)
        {
            if (!delveCost.CanPay(caster, totalCost))
            {
                throw new InvalidOperationException(
                    $"Cannot pay Delve cost for {card.Name}: " +
                    $"selection of {delveCost.ReductionAmount} card(s) " +
                    $"invalid (generic={totalCost.Generic}).");
            }
            totalCost = delveCost.ApplyTo(totalCost);
            delveCost.Pay(caster);

            // CR 702.66 — stamp the count of delve-exiled cards on the card
            // itself so downstream ETB-with-counters effects (Murktide Regent
            // — CR 122.1g X-counter ETB) can read "cards exiled with me"
            // without us re-plumbing DelveCost across the spell-cast →
            // permanent boundary. Consumed + cleared by the ETB effect.
            if (card is Card concreteCard)
            {
                concreteCard.SetPendingDelveExiledCount(delveCost.ReductionAmount);
            }
        }

        // CR 601.2g — mana sourcing. When the caller has already prompted +
        // paid mana (TurnDriver does this so a failed pay can rotate the
        // hand instead of mutating the stack), reuse that ManaPayment as
        // metadata so the agent isn't asked twice (visible UX bug: double
        // mana prompt). Otherwise prompt here as the canonical caster.
        var mana = preChosenMana
            ?? await agent.ChooseManaSourcesAsync(ctx, totalCost, ct);

        var chosen = new ChosenSpellParams(
            mode, xValue, collectedTargets, mana, ctx.AllPlayers,
            ModeIndexes: null,
            AdditionalCostPayments: mergedAdditional.Count > 0 ? mergedAdditional : null);
        var effects = definition.EffectFactory(chosen);

        // If casting via alternative cost (e.g. Flashback), card may not be in
        // hand — move it from whatever zone it's in.
        _zoneService.MoveCard(card, card.Zone, ZoneType.Stack, controller: caster);

        // Wrap effects so the alternative cost's OnResolved fires after the
        // spell's printed effects (CR 702.34b style).
        IReadOnlyList<IEffect> finalEffects = effects;
        if (alternativeCost != null)
        {
            var wrapped = effects.Append(new Effect(
                $"{alternativeCost.Description} cleanup",
                () => alternativeCost.OnResolved(card, caster))).ToList();
            finalEffects = wrapped;
        }

        var spell = new Spells.Spell(card, caster, effects: finalEffects);
        _stack.Push(spell);
        _eventBus.Publish(new SpellCastEvent(spell));

        return spell;
    }
}
