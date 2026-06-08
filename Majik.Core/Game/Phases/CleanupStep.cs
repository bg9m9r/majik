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
public class CleanupStep : StepState
{
    private readonly IEventBus? _eventBus;
    private readonly ZoneService? _zoneService;

    public CleanupStep(IEventBus? eventBus = null, ZoneService? zoneService = null) 
        : base(StepStateType.Cleanup, eventBus)
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
    /// CR 514.1 — discard down to maximum hand size for the active player.
    /// Routes each discard through <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>
    /// (<c>wasCost: false</c> — a cleanup trim is not a cost) so a
    /// <see cref="DiscardedEvent"/> fires per card and "Whenever you discard a
    /// card …" triggers observe the cleanup discard. The event publishes on
    /// the step's <see cref="_eventBus"/> when supplied, otherwise the player's
    /// registered bus (best-effort).
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
            // Simplified: discard from end (v1 deterministic pick — agent-driven
            // choice deferred behind the same queue as Fx.Discard).
            for (int i = 0; i < cardsToDiscard; i++)
            {
                var card = cards[cards.Count - 1 - i];
                Majik.Core.Primitives.Fx.DiscardCard(player, card, wasCost: false, _eventBus);
            }
        }
    }
}
