using Majik.Core.Abilities;
using Majik.Core.Cards;
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
///   1. announce spell, move card from hand to stack
///   2. choose modes
///   3. choose X (variable costs); X is added to the mana cost
///   4. choose targets
///   5. choose mana payment
///   6. build Spell with chosen effects, push onto stack
///   7. publish <see cref="SpellCastEvent"/>
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

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the spell can't be
    /// cast right now (CR 117.1). Caller (priority loop) catches and
    /// re-prompts the agent for a different action.
    /// </summary>
    public async Task<Spells.Spell> CastAsync(
        Player caster,
        ICard card,
        SpellDefinition definition,
        IPlayerAgent agent,
        GameContext ctx,
        CancellationToken ct = default)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (agent == null) throw new ArgumentNullException(nameof(agent));

        // CR 117.1 — sorcery-speed gating.
        if (ctx.CurrentPhase.HasValue
            && !CastingPermission.CanCast(card, caster, ctx.ActivePlayer,
                ctx.CurrentPhase.Value, _stack.IsEmpty, out var reason))
        {
            throw new InvalidOperationException($"Cannot cast {card.Name}: {reason}");
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
            collectedTargets.Add(picked);
        }

        // Cost = printed mana cost + X (generic) if applicable.
        var totalCost = ManaCost.Parse(card.ManaCost);
        if (xValue.HasValue && xValue.Value > 0)
        {
            totalCost = totalCost.AddGenericCost(xValue.Value);
        }

        var mana = await agent.ChooseManaSourcesAsync(ctx, totalCost, ct);

        var chosen = new ChosenSpellParams(mode, xValue, collectedTargets, mana);
        var effects = definition.EffectFactory(chosen);

        _zoneService.MoveCardTo(card, ZoneType.Stack, controller: caster);

        var spell = new Spells.Spell(card, caster, effects: effects);
        _stack.Push(spell);
        _eventBus.Publish(new SpellCastEvent(spell));

        return spell;
    }
}
