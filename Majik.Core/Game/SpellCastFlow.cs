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
        IAlternativeCost? alternativeCost = null)
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

        // CR 601.2f — additional costs first, before mana payment.
        if (additionalCosts != null)
        {
            foreach (var addCost in additionalCosts)
            {
                if (!addCost.Pay(caster))
                {
                    throw new InvalidOperationException(
                        $"Failed to pay additional cost: {addCost.Description}");
                }
            }
        }

        int? mode = null;
        if (definition.Modes.Count > 0)
        {
            mode = await agent.ChooseModeAsync(ctx, definition.Modes, ct);
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

        // Cost — printed + X, OR alternative cost when supplied.
        var totalCost = alternativeCost?.AlternativeManaCost ?? ManaCost.Parse(card.ManaCost);
        if (xValue.HasValue && xValue.Value > 0)
        {
            totalCost = totalCost.AddGenericCost(xValue.Value);
        }

        var mana = await agent.ChooseManaSourcesAsync(ctx, totalCost, ct);

        var chosen = new ChosenSpellParams(mode, xValue, collectedTargets, mana);
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
