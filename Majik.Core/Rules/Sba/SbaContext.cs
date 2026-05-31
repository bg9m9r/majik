using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.Rules.Sba;

/// <summary>
/// Per-pass state passed to every <see cref="IStateBasedActionCheck"/>.
/// The card list is refreshed by the coordinator between passes (via
/// <see cref="SetCards"/>) so each check observes the post-mutation world.
///
/// The materialized <see cref="Cards"/> list and the
/// <see cref="Permanents"/> / <see cref="Creatures"/> projections are
/// computed ONCE per pass and shared across every check, replacing the
/// old per-check <c>OfType&lt;…&gt;().ToList()</c> re-materializations
/// (O(permanents) per check → O(permanents) per pass). Re-materialization
/// only happens when the coordinator hands a check a moved card (i.e. when
/// the fixed-point loop is still making progress).
/// </summary>
public sealed class SbaContext
{
    private IReadOnlyList<ICard> _cards;
    private IReadOnlyList<Permanent>? _permanents;
    private IReadOnlyList<Creature>? _creatures;

    public IReadOnlyList<Player> Players { get; }

    /// <summary>
    /// All cards in scope for this pass. Reassigning (via
    /// <see cref="SetCards"/>) invalidates the cached
    /// <see cref="Permanents"/> / <see cref="Creatures"/> projections.
    /// </summary>
    public IReadOnlyList<ICard> Cards => _cards;

    /// <summary>
    /// The <see cref="Permanent"/> subset of <see cref="Cards"/>, materialized
    /// once per pass and shared by every check that needs it.
    /// </summary>
    public IReadOnlyList<Permanent> Permanents => _permanents ??= BuildPermanents();

    /// <summary>
    /// The <see cref="Creature"/> subset of <see cref="Cards"/>, materialized
    /// once per pass and shared by every check that needs it.
    /// </summary>
    public IReadOnlyList<Creature> Creatures => _creatures ??= BuildCreatures();

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
        _cards = cards;
        EventBus = eventBus;
        ZoneService = zoneService;
        TriggerManager = triggerManager;
        Replacements = replacements;
    }

    /// <summary>
    /// Replace the per-pass card list and invalidate the cached projections.
    /// Called by the coordinator between fixed-point passes.
    /// </summary>
    internal void SetCards(IReadOnlyList<ICard> cards)
    {
        _cards = cards;
        _permanents = null;
        _creatures = null;
    }

    private List<Permanent> BuildPermanents()
    {
        var result = new List<Permanent>();
        foreach (var card in _cards)
        {
            if (card is Permanent p) result.Add(p);
        }
        return result;
    }

    private List<Creature> BuildCreatures()
    {
        var result = new List<Creature>();
        foreach (var card in _cards)
        {
            if (card is Creature c) result.Add(c);
        }
        return result;
    }
}
