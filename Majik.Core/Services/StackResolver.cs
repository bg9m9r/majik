using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.Zones;

namespace Majik.Core.Services;

/// <summary>
/// Handles resolution of stack objects.
/// Implements Magic: The Gathering stack resolution rules (Rule 405, 608).
/// </summary>
public class StackResolver
{
    private readonly IEventBus? _eventBus;
    private readonly ZoneService? _zoneService;
    private readonly StateBasedActions? _stateBasedActions;

    public StackResolver(IEventBus? eventBus = null, ZoneService? zoneService = null, StateBasedActions? stateBasedActions = null)
    {
        _eventBus = eventBus;
        _zoneService = zoneService;
        _stateBasedActions = stateBasedActions;
    }

    /// <summary>
    /// Resolve the top object on the stack (synchronous shim over
    /// <see cref="ResolveTopAsync"/>; CR 608). Retained so callers not yet
    /// converted to the async driver keep working.
    /// </summary>
    public IStackObject? ResolveTop(Majik.Core.Stack.Stack stack)
        => ResolveTopAsync(stack).GetAwaiter().GetResult();

    /// <summary>
    /// PLAN 01 — resolve the top object on the stack on the async path
    /// (CR 608). The CR 603.4 intervening-if recheck and the CR 608.2b
    /// target-legality recheck stay SYNCHRONOUS and run before the await, so
    /// resolution ordering is unchanged. The resolving object's effects are
    /// awaited via <see cref="IStackObject.ResolveAsync"/>, threading the
    /// agent for the object's controller (resolved via
    /// <paramref name="agentLookup"/>) plus the live <paramref name="game"/>.
    /// </summary>
    /// <param name="agentLookup">
    /// Maps a controller to its agent for the resolution context. Null (the
    /// default) leaves the agent unbound — effects on the legacy sync path
    /// don't read it. The async driver supplies <c>AgentRegistry.Get</c>.
    /// </param>
    /// <param name="game">Live game context for async effects; may be null.</param>
    public async Task<IStackObject?> ResolveTopAsync(
        Majik.Core.Stack.Stack stack,
        Func<Player, IPlayerAgent?>? agentLookup = null,
        GameContext? game = null,
        CancellationToken ct = default)
    {
        if (stack == null)
        {
            throw new ArgumentNullException(nameof(stack));
        }

        if (stack.IsEmpty)
        {
            return null;
        }

        // Pop the object from stack and resolve it
        var top = stack.Pop();
        if (top != null)
        {
            // Rule 603.4: re-check intervening-if for triggered abilities. If false,
            // the ability is removed from the stack and its effects do not occur.
            // SYNCHRONOUS — runs before the resolution await (CR 603.4 / 608.2b).
            if (top is ITriggeredAbility triggered && !triggered.CanBePutOnStack())
            {
                _eventBus?.Publish(new TriggeredAbilityCounteredEvent(
                    triggered, "intervening-if failed at resolution"));
                return top;
            }

            // CR 608.2b — spell with at least one chosen target: if every
            // target is now illegal, the spell is countered on resolution.
            // SYNCHRONOUS — runs before the resolution await.
            if (top is Majik.Core.Spells.Spell spellRecheck
                && spellRecheck.ChosenTargets.Count > 0
                && spellRecheck.TargetLegalityPredicate != null)
            {
                var anyLegal = spellRecheck.ChosenTargets
                    .Any(t => spellRecheck.TargetLegalityPredicate(t));
                if (!anyLegal)
                {
                    spellRecheck.Card.SetZone(Zones.ZoneType.Graveyard);
                    _eventBus?.Publish(new StateBasedActionExecutedEvent(
                        $"{spellRecheck.Card.Name} countered: all targets illegal"));
                    return top;
                }
            }

            // Resolve the object (Rule 608.1) on the async path.
            var agent = agentLookup?.Invoke(top.Controller);
            await top.ResolveAsync(agent, game, ct).ConfigureAwait(false);

            // Handle spell resolution (Rule 608.2)
            if (top is ISpell spell)
            {
                HandleSpellResolution(spell);
            }
            // Handle ability resolution (Rule 608.2)
            else if (top is IActivatedAbility ability)
            {
                HandleAbilityResolution(ability);
            }
            else if (top is ITriggeredAbility triggeredAbility)
            {
                HandleTriggeredAbilityResolution(triggeredAbility);
            }

            // Fire resolution event
            _eventBus?.Publish(new StackObjectResolvedEvent(top));

            // Check state-based actions after resolution (Rule 704.1)
            // Note: In a full implementation, we'd need access to all players and cards
            // For now, this is a placeholder - SBA checking will be done at higher levels
        }

        return top;
    }

    /// <summary>
    /// Handle spell resolution - move card to appropriate zone (Rule 608.2).
    /// </summary>
    private void HandleSpellResolution(ISpell spell)
    {
        if (spell == null)
        {
            return;
        }

        var card = spell.Card;
        var destinationZone = GetSpellDestinationZone(spell);

        // Move card from stack to destination zone through ZoneService so
        // the destination zone's collection is updated AND CardMovedEvent
        // fires (downstream listeners — combat, triggers, log — rely on
        // these events). Direct card.Zone mutation skipped owner-zone
        // bookkeeping and silently created limbo permanents.
        if (_zoneService != null && card.Owner != null)
        {
            _zoneService.MoveCardTo(card, destinationZone,
                destinationZone == ZoneType.Battlefield ? spell.Controller : null);
        }
        else
        {
            card.SetZone(destinationZone);
            if (destinationZone == ZoneType.Battlefield)
            {
                card.SetController(spell.Controller);
            }
        }
    }

    /// <summary>
    /// Get the zone the spell should move to after resolution.
    /// Permanents go to battlefield, instants/sorceries go to graveyard (Rule 608.2).
    /// An alt-cost may override this via <see cref="Spell.PostResolutionZoneOverride"/>
    /// (CR 715.3d — Adventure spells exile instead of going to graveyard /
    /// battlefield; the override is stamped by <see cref="Game.SpellCastFlow"/>
    /// from <see cref="Costs.IAlternativeCost.PostResolutionZone"/>).
    /// </summary>
    private ZoneType GetSpellDestinationZone(ISpell spell)
    {
        // CR 715.3d / Flashback / similar — alt-cost-supplied destination
        // override wins over the printed-type default. We consult Spell
        // directly here (rather than re-threading the alt-cost through
        // StackResolver) because Spell is the only object on the stack
        // and SpellCastFlow already stamps the field at cast time.
        if (spell is Majik.Core.Spells.Spell concrete && concrete.PostResolutionZoneOverride.HasValue)
        {
            return concrete.PostResolutionZoneOverride.Value;
        }

        var card = spell.Card;

        // Permanents go to battlefield
        if (card.HasType(Cards.Types.CardType.Creature) ||
            card.HasType(Cards.Types.CardType.Land) ||
            card.HasType(Cards.Types.CardType.Enchantment) ||
            card.HasType(Cards.Types.CardType.Artifact) ||
            card.HasType(Cards.Types.CardType.Planeswalker))
        {
            return ZoneType.Battlefield;
        }

        // Instants and sorceries go to graveyard
        if (card.HasType(Cards.Types.CardType.Instant) || card.HasType(Cards.Types.CardType.Sorcery))
        {
            return ZoneType.Graveyard;
        }

        // Default to graveyard
        return ZoneType.Graveyard;
    }

    /// <summary>
    /// Handle ability resolution - execute ability effects (Rule 608.2).
    /// </summary>
    private void HandleAbilityResolution(IActivatedAbility ability)
    {
        if (ability == null)
        {
            return;
        }

        // Ability effects are executed during Resolve()
        // Additional handling can be added here if needed
    }

    /// <summary>
    /// Handle triggered ability resolution - execute ability effects (Rule 608.2).
    /// </summary>
    private void HandleTriggeredAbilityResolution(ITriggeredAbility ability)
    {
        if (ability == null)
        {
            return;
        }

        // Triggered ability effects are executed during Resolve()
        // Additional handling can be added here if needed
    }

    /// <summary>
    /// Check if the stack can be resolved (has objects).
    /// </summary>
    public bool CanResolve(Majik.Core.Stack.Stack stack)
    {
        if (stack == null)
        {
            return false;
        }

        return !stack.IsEmpty;
    }

    /// <summary>
    /// Resolve all objects on the stack (used for testing/debugging).
    /// Synchronous shim over <see cref="ResolveAllAsync"/>.
    /// </summary>
    public void ResolveAll(Majik.Core.Stack.Stack stack)
        => ResolveAllAsync(stack).GetAwaiter().GetResult();

    /// <summary>
    /// PLAN 01 — resolve every object on the stack on the async path, one at
    /// a time in LIFO order (CR 608.1). Threads the agent lookup + live game
    /// through each <see cref="ResolveTopAsync"/> call.
    /// </summary>
    public async Task ResolveAllAsync(
        Majik.Core.Stack.Stack stack,
        Func<Player, IPlayerAgent?>? agentLookup = null,
        GameContext? game = null,
        CancellationToken ct = default)
    {
        if (stack == null)
        {
            throw new ArgumentNullException(nameof(stack));
        }

        while (!stack.IsEmpty)
        {
            await ResolveTopAsync(stack, agentLookup, game, ct).ConfigureAwait(false);
        }
    }
}
