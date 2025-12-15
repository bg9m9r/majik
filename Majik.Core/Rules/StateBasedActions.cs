using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Rules;

/// <summary>
/// Service for checking and executing state-based actions (Rule 704).
/// State-based actions are checked whenever a player would receive priority.
/// </summary>
public class StateBasedActions
{
    private readonly IEventBus? _eventBus;

    public StateBasedActions(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// Check and execute all state-based actions.
    /// </summary>
    public void CheckStateBasedActions(IEnumerable<Player> players, IEnumerable<ICard> allCards)
    {
        if (players == null || allCards == null)
        {
            return;
        }

        var playerList = players.ToList();
        var cardList = allCards.ToList();

        // Check each state-based action
        CheckPlayerLife(playerList);
        CheckCreatureDeath(cardList);
        CheckPlaneswalkerDeath(cardList);
        // TODO: Legend rule, Planeswalker uniqueness rule, etc.
    }

    /// <summary>
    /// Check if any player has lost (0 or less life) (Rule 704.5).
    /// </summary>
    private void CheckPlayerLife(IEnumerable<Player> players)
    {
        foreach (var player in players)
        {
            if (player.LifeTotal <= 0 && !player.HasLost)
            {
                player.HasLost = true;
                _eventBus?.Publish(new PlayerLostEvent(player));
                _eventBus?.Publish(new StateBasedActionExecutedEvent("Player lost due to 0 or less life"));
            }
        }
    }

    /// <summary>
    /// Check if any creatures have died (damage >= toughness) (Rule 704.5f).
    /// </summary>
    private void CheckCreatureDeath(IEnumerable<ICard> allCards)
    {
        var creatures = allCards.OfType<Cards.Creature>().ToList();

        foreach (var creature in creatures)
        {
            if (creature.IsDead() && creature.Zone == ZoneType.Battlefield)
            {
                // TODO: Move creature to graveyard (use ZoneService)
                // For now, just mark for removal
                creature.Zone = ZoneType.Graveyard;
                _eventBus?.Publish(new StateBasedActionExecutedEvent($"Creature {creature.Name} died"));
            }
        }
    }

    /// <summary>
    /// Check if any planeswalkers have died (0 loyalty) (Rule 704.5j).
    /// </summary>
    private void CheckPlaneswalkerDeath(IEnumerable<ICard> allCards)
    {
        var planeswalkers = allCards.OfType<Cards.Planeswalker>().ToList();

        foreach (var planeswalker in planeswalkers)
        {
            if (planeswalker.IsDead() && planeswalker.Zone == ZoneType.Battlefield)
            {
                // TODO: Move planeswalker to graveyard (use ZoneService)
                // For now, just mark for removal
                planeswalker.Zone = ZoneType.Graveyard;
                _eventBus?.Publish(new StateBasedActionExecutedEvent($"Planeswalker {planeswalker.Name} died"));
            }
        }
    }
}
