using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Harbinger of the Seas (Modern Horizons 2).
///
/// Creature — Wizard {1}{U}
/// 2/2
/// Oracle text: "Nonbasic lands are Islands."
///
/// ## Implementation
///
/// Same Layer 4 machinery as Blood Moon (CR 305.6 / 613.1d), but retypes
/// nonbasic lands to <see cref="CardSubtype.Island"/> instead of Mountain.
/// Wired via the shared <see cref="RetypeLandsStaticEffect"/> binder.
///
/// ## Subtypes
///
/// Harbinger of the Seas's printed creature type is "Merfolk Wizard."
/// The Majik <see cref="CardSubtype"/> enum does not yet include
/// <c>Merfolk</c>; per project policy (don't invent subtypes), only
/// <see cref="CardSubtype.Wizard"/> is assigned. If/when Merfolk is added
/// to <see cref="CardSubtype"/>, this factory should be updated to
/// include it.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so
/// the effect is attached to the game's continuous-effects service.
/// </summary>
public static class HarbingerOfTheSeasFactory
{
    public const string CardName = "Harbinger of the Seas";
    public const string Cost = "{1}{U}";

    private static readonly IReadOnlySet<CardSubtype> IslandOnly =
        new HashSet<CardSubtype> { CardSubtype.Island };

    /// <summary>
    /// Creates a Harbinger of the Seas with correct card identity only
    /// (no live Layer 4 effect). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Harbinger of the Seas. When
    /// <paramref name="effects"/> is supplied, a
    /// <see cref="RetypeLandsStaticEffect"/> is attached so the Layer 4
    /// effect registers/unregisters as the Harbinger enters/leaves the
    /// battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            CardName,
            Cost,
            power: 2,
            toughness: 2,
            // Printed creature type "Merfolk Wizard" — Merfolk not yet
            // enumerated in CardSubtype, so we assign Wizard only.
            subtypes: new[] { CardSubtype.Wizard });
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            // CR 305.6 — "Nonbasic lands are Islands." Same nonbasic-Land
            // scope as Blood Moon; only the target subtype differs.
            var lifecycle = new RetypeLandsStaticEffect(
                card,
                effects,
                eventBus,
                scope: p => p is Land && !p.HasSupertype(CardSupertype.Basic),
                newLandSubtypes: IslandOnly);
            lifecycle.Attach();
        }

        return card;
    }
}
