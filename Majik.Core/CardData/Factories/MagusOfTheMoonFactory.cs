using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magus of the Moon (Time Spiral / multiple reprints).
///
/// Creature — Human Wizard {2}{R}
/// 2/2
/// Oracle text: "Nonbasic lands are Mountains."
///
/// ## Implementation
///
/// Same Layer 4 type-changing effect as Blood Moon (CR 305.6 / 613.1d):
/// every nonbasic Land becomes a Mountain while Magus of the Moon is on
/// the battlefield. Wired via the shared
/// <see cref="RetypeLandsStaticEffect"/> binder — same scope predicate
/// (nonbasic Land) and same new subtype set ({Mountain}) as Blood Moon.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so
/// the effect is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity but no live effect — suitable for pure card-shape
/// tests.
/// </summary>
public static class MagusOfTheMoonFactory
{
    public const string CardName = "Magus of the Moon";
    public const string Cost = "{2}{R}";

    private static readonly IReadOnlySet<CardSubtype> MountainOnly =
        new HashSet<CardSubtype> { CardSubtype.Mountain };

    /// <summary>
    /// Creates a Magus of the Moon with correct card identity only (no
    /// live Layer 4 effect). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Magus of the Moon. When
    /// <paramref name="effects"/> is supplied, a
    /// <see cref="RetypeLandsStaticEffect"/> is attached so the Layer 4
    /// effect registers/unregisters as Magus of the Moon enters/leaves the
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            // CR 305.6 — "Nonbasic lands are Mountains." Same scope +
            // target subtype as Blood Moon.
            var lifecycle = new RetypeLandsStaticEffect(
                card,
                effects,
                eventBus,
                scope: p => p is Land && !p.HasSupertype(CardSupertype.Basic),
                newLandSubtypes: MountainOnly);
            lifecycle.Attach();
        }

        return card;
    }
}
