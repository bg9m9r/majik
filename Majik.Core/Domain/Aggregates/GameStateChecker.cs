using Majik.Core.Cards;
using Majik.Core.Domain.Aggregates;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Spells;

namespace Majik.Core.Domain.Aggregates;

/// <summary>
/// Helper class for checking state-based actions throughout the game.
/// Provides a centralized way to check SBAs with access to all game state.
/// </summary>
public class GameStateChecker
{
    private readonly StateBasedActions _stateBasedActions;
    private readonly Game _game;

    public GameStateChecker(Game game, StateBasedActions stateBasedActions)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _stateBasedActions = stateBasedActions ?? throw new ArgumentNullException(nameof(stateBasedActions));
    }

    /// <summary>
    /// Check state-based actions with current game state.
    /// </summary>
    public void CheckStateBasedActions()
    {
        var players = _game.Players;
        var allCards = GetAllCardsInGame();
        _stateBasedActions.CheckStateBasedActions(players, allCards);
    }

    /// <summary>
    /// Get all cards in the game from all zones.
    /// </summary>
    private IEnumerable<ICard> GetAllCardsInGame()
    {
        var allCards = new List<ICard>();

        foreach (var player in _game.Players)
        {
            // Get cards from all zones
            allCards.AddRange(player.Zones.Library.GetCards());
            allCards.AddRange(player.Zones.Hand.GetCards());
            allCards.AddRange(player.Zones.Battlefield.GetCards());
            allCards.AddRange(player.Zones.Graveyard.GetCards());
            allCards.AddRange(player.Zones.Exile.GetCards());
        }

        // Add cards from stack
        if (_game.IsStarted)
        {
            allCards.AddRange(_game.Stack.GetAll().OfType<ISpell>().Select(s => s.Card));
        }

        return allCards;
    }
}
