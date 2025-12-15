using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game.Phases;

/// <summary>
/// Draw step implementation.
/// Active player draws a card from their library.
/// </summary>
public class DrawStep : PhaseState
{
    private readonly IEventBus? _eventBus;
    private readonly ZoneService? _zoneService;

    public DrawStep(IEventBus? eventBus = null, ZoneService? zoneService = null) 
        : base(PhaseStateType.Draw, eventBus)
    {
        _eventBus = eventBus;
        _zoneService = zoneService;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        // Draw logic will be implemented when we have cards in library
        // For now, just fire the event
    }

    public override void OnExit()
    {
        base.OnExit();
    }

    /// <summary>
    /// Draw a card for the active player.
    /// </summary>
    public void DrawCard(Player player)
    {
        if (player == null)
        {
            return;
        }

        var library = player.Zones.Library;
        var cards = library.GetCards().ToList();
        
        if (cards.Count > 0)
        {
            var card = cards[0]; // Draw from top (simplified)
            _zoneService?.MoveCardTo(card, ZoneType.Hand);
            _eventBus?.Publish(new CardDrawnEvent(card, player));
        }
    }
}
