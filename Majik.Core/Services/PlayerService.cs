using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.Services;

/// <summary>
/// Domain service for managing player operations.
/// Handles life changes, player loss, and other player-related operations.
/// </summary>
public class PlayerService
{
    private readonly IEventBus? _eventBus;

    public PlayerService(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// Create a new player.
    /// </summary>
    public Player CreatePlayer(string name, int startingLife = 20)
    {
        return new Player(name, startingLife);
    }

    /// <summary>
    /// Make a player gain life.
    /// </summary>
    public void GainLife(Player player, int amount)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (amount < 0)
        {
            throw new ArgumentException("Amount must be non-negative", nameof(amount));
        }

        if (player.HasLost)
        {
            throw new InvalidPlayerActionException("Cannot gain life after losing the game");
        }

        var oldLife = player.LifeTotal;
        player.LifeTotal += amount;

        // Publish domain event
        _eventBus?.Publish(new LifeChangedEvent(player, oldLife, player.LifeTotal));
    }

    /// <summary>
    /// Make a player lose life.
    /// </summary>
    public void LoseLife(Player player, int amount)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (amount < 0)
        {
            throw new ArgumentException("Amount must be non-negative", nameof(amount));
        }

        if (player.HasLost)
        {
            throw new InvalidPlayerActionException("Cannot lose life after losing the game");
        }

        var oldLife = player.LifeTotal;
        player.LifeTotal -= amount;

        // Check if player has lost
        if (player.LifeTotal <= 0)
        {
            player.HasLost = true;
        }

        // Publish domain event
        _eventBus?.Publish(new LifeChangedEvent(player, oldLife, player.LifeTotal));

        // Check if player lost
        if (player.HasLost)
        {
            _eventBus?.Publish(new PlayerLostEvent(player));
        }
    }

    /// <summary>
    /// Set a player's life total.
    /// </summary>
    public void SetLifeTotal(Player player, int newLife)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        if (player.HasLost)
        {
            throw new InvalidPlayerActionException("Cannot change life total after losing the game");
        }

        var oldLife = player.LifeTotal;
        player.LifeTotal = newLife;

        // Check if player has lost
        if (player.LifeTotal <= 0)
        {
            player.HasLost = true;
        }

        // Publish domain event
        _eventBus?.Publish(new LifeChangedEvent(player, oldLife, player.LifeTotal));

        // Check if player lost
        if (player.HasLost)
        {
            _eventBus?.Publish(new PlayerLostEvent(player));
        }
    }
}
