using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.Rules.Sba;

/// <summary>
/// Per-pass state passed to every <see cref="IStateBasedActionCheck"/>.
/// The card list is refreshed by the coordinator between passes so each
/// check observes the post-mutation world.
/// </summary>
public sealed class SbaContext
{
    public IReadOnlyList<Player> Players { get; }
    public IReadOnlyList<ICard> Cards { get; internal set; }
    public IEventBus? EventBus { get; }
    public ZoneService? ZoneService { get; }
    public TriggerManager? TriggerManager { get; }
    public ReplacementBus? Replacements { get; }

    public SbaContext(
        IReadOnlyList<Player> players,
        IReadOnlyList<ICard> cards,
        IEventBus? eventBus,
        ZoneService? zoneService,
        TriggerManager? triggerManager,
        ReplacementBus? replacements)
    {
        Players = players;
        Cards = cards;
        EventBus = eventBus;
        ZoneService = zoneService;
        TriggerManager = triggerManager;
        Replacements = replacements;
    }
}
