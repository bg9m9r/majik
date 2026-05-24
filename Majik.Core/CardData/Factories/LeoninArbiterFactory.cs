using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leonin Arbiter (Scars of Mirrodin / various, {1}{W}).
///
/// Creature — Cat Cleric 2/2. Oracle text:
///   "Players can't search their libraries unless they pay {2}."
///
/// ## Implemented (v1)
/// - 2/2 Cat Cleric at {1}{W} with correct identity / owner / controller.
/// - <b>Printed static (structural shape, CR 614)</b>: a
///   <see cref="LeoninArbiterSearchRestrictionEffect"/> marker is registered
///   on the supplied <see cref="ContinuousEffectsService"/> while Leonin
///   Arbiter is on the battlefield. The marker is detectable via
///   <c>effects.OfType&lt;LeoninArbiterSearchRestrictionEffect&gt;</c> for
///   future enforcement code.
///
/// ## Deferred (v1 gaps)
/// - <b>Search enforcement</b>: the engine has no unified "search library"
///   surface yet — tutor and fetch-land search paths are implemented
///   individually with no shared interception point. When a
///   SearchLibraryService or similar is introduced, it should query the
///   <see cref="ContinuousEffectsService"/> for active
///   <see cref="LeoninArbiterSearchRestrictionEffect"/> instances and
///   require each searching player to pay {2} per such instance before
///   allowing the search to proceed (CR 701.19 / CR 118.7 — optional
///   additional cost).
/// </summary>
[CardName("Leonin Arbiter")]
public static class LeoninArbiterFactory
{
    public const string CardName = "Leonin Arbiter";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Leonin Arbiter with no runtime services. No restriction
    /// marker is registered. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, continuousEffectsService: null);

    /// <summary>
    /// Construct Leonin Arbiter with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="continuousEffectsService">When supplied, a
    /// <see cref="LeoninArbiterSearchRestrictionEffect"/> marker is attached
    /// and registered on this service so enforcement code can detect the
    /// restriction. When null, the marker is not wired (shape-only path).</param>
    /// <param name="eventBus">Event bus used by the restriction lifecycle
    /// to track zone changes. May be null — the restriction will be synced
    /// once at Attach time but won't react to subsequent zone moves.</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffectsService,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffectsService != null)
        {
            // v1: structural marker — registers on battlefield entry,
            // unregisters on exit. Actual search enforcement is deferred
            // pending a unified library-search hook (see class xmldoc).
            var restriction = new LeoninArbiterSearchRestrictionEffect(
                source: card,
                effects: continuousEffectsService,
                eventBus: eventBus);
            restriction.Attach();
        }

        return card;
    }
}
