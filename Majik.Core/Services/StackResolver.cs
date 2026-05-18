using Majik.Core.Abilities;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
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
    /// Resolve the top object on the stack.
    /// </summary>
    public IStackObject? ResolveTop(Majik.Core.Stack.Stack stack)
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
            if (top is ITriggeredAbility triggered && !triggered.CanBePutOnStack())
            {
                _eventBus?.Publish(new TriggeredAbilityCounteredEvent(
                    triggered, "intervening-if failed at resolution"));
                return top;
            }

            // Resolve the object (Rule 608.1)
            top.Resolve();

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

        // Move card from stack to destination zone
        if (_zoneService != null && card.Owner != null)
        {
            // Use zone service to move card
            // For now, just update the zone property
            // Full zone service integration will be added
            card.Zone = destinationZone;

            // If moving to battlefield, set controller
            if (destinationZone == ZoneType.Battlefield)
            {
                card.Controller = spell.Controller;
            }
        }
        else
        {
            // Fallback: just update zone
            card.Zone = destinationZone;
            if (destinationZone == ZoneType.Battlefield)
            {
                card.Controller = spell.Controller;
            }
        }
    }

    /// <summary>
    /// Get the zone the spell should move to after resolution.
    /// Permanents go to battlefield, instants/sorceries go to graveyard (Rule 608.2).
    /// </summary>
    private ZoneType GetSpellDestinationZone(ISpell spell)
    {
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
    /// </summary>
    public void ResolveAll(Majik.Core.Stack.Stack stack)
    {
        if (stack == null)
        {
            throw new ArgumentNullException(nameof(stack));
        }

        while (!stack.IsEmpty)
        {
            ResolveTop(stack);
        }
    }
}
