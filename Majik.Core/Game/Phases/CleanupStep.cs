using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game.Phases;

/// <summary>
/// Cleanup step implementation.
/// Discard to hand size, remove damage, end turn.
/// </summary>
public class CleanupStep : PhaseState
{
    private readonly IEventBus? _eventBus;
    private readonly ZoneService? _zoneService;

    public CleanupStep(IEventBus? eventBus = null, ZoneService? zoneService = null) 
        : base(PhaseStateType.Cleanup, eventBus)
    {
        _eventBus = eventBus;
        _zoneService = zoneService;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        // Cleanup logic will be implemented when we have hand size limits
        // For now, just fire the event
    }

    public override void OnExit()
    {
        base.OnExit();
    }

    /// <summary>
    /// Discard to hand size for the active player.
    /// </summary>
    public void DiscardToHandSize(Player player, int maxHandSize = 7)
    {
        if (player == null)
        {
            return;
        }

        var hand = player.Zones.Hand;
        var cards = hand.GetCards().ToList();
        
        if (cards.Count > maxHandSize)
        {
            var cardsToDiscard = cards.Count - maxHandSize;
            // Simplified: discard from end
            for (int i = 0; i < cardsToDiscard; i++)
            {
                var card = cards[cards.Count - 1 - i];
                _zoneService?.MoveCardTo(card, ZoneType.Graveyard);
            }
        }
    }
}
