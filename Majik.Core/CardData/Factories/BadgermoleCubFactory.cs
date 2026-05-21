using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Badgermole Cub (Bloomburrow).
///
/// Creature — Bear {G} 1/1. Oracle text:
///   "When this creature enters, earthbend 1. (Target land you control becomes
///    a 0/0 creature with haste that's still a land. Put a +1/+1 counter on it.
///    When it dies or is exiled, return it to the battlefield tapped.)
///    Whenever you tap a creature for mana, add an additional {G}."
///
/// ## Implemented (v1)
/// - Correct name, type (Creature), subtype (Bear), mana cost ({G}),
///   power/toughness (1/1). Shell only — zero abilities attached.
///
/// ## Deferred (v1 gaps)
/// - <b>Earthbend 1 ETB</b>: the earthbend keyword (Bloomburrow mechanic)
///   animates a target land the controller controls into a 0/0 creature with
///   haste, adds a +1/+1 counter, and registers a delayed triggered ability
///   that returns the land to the battlefield tapped when it leaves. No
///   animate-land infrastructure exists yet (no land→creature conversion,
///   no delayed-trigger system, no counter-on-land support). Deferred until
///   those systems are in place.
/// - <b>"Whenever you tap a creature for mana, add {G}"</b>: requires a
///   tap-for-mana watcher (intercept ManaAbility activations where the source
///   is a creature) and an extra-mana production side effect. No mana-ability
///   tap-watcher infrastructure exists. Deferred.
/// </summary>
public static class BadgermoleCubFactory
{
    /// <summary>
    /// Construct Badgermole Cub owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var b = new Creature(
            "Badgermole Cub",
            manaCost: "{G}",
            power: 1, toughness: 1,
            subtypes: new[] { CardSubtype.Bear });
        b.SetOwner(owner);
        b.SetController(owner);

        // No abilities attached in v1 — both are deferred (see xmldoc above).

        return b;
    }
}
