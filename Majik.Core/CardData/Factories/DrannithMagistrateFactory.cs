using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Drannith Magistrate (Ikoria: Lair of Behemoths,
/// {1}{W}).
///
/// Creature — Human Wizard, 1/3.
/// Oracle text:
///   "Your opponents can't cast spells from anywhere other than their
///    hands." (CR 113.6 / CR 601.2a)
///
/// ## Implemented (v1)
/// - Creature with mana cost {1}{W}, P/T 1/3, Human + Wizard subtypes
///   and correct identity / owner / controller.
/// - <b>Printed static</b> (CR 113.6): cast-from-hand-only restriction
///   on each opponent. Wired via
///   <see cref="CastFromHandOnlyRestrictionEffect"/>: while Drannith
///   Magistrate is on the battlefield, every player returned by
///   <c>opponentResolver</c> is registered into
///   <see cref="Majik.Core.Rules.CastingRestrictions"/>, and
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects their casts
///   whose declared source zone (<see cref="CastSpellAction.FromZone"/>)
///   is not the hand. The effect detaches as the Magistrate leaves the
///   battlefield via <see cref="CardMovedEvent"/> on the supplied bus.
///
/// ## Deferred (v1 gaps)
/// - <b>Ambient zone stamping</b>: <see cref="CastSpellAction"/> now
///   carries an optional <c>FromZone</c>, but the production
///   <see cref="Majik.Core.Services.SpellCaster"/> /
///   <see cref="Majik.Core.Domain.Flows.SpellCastFlow"/> pipeline does
///   not yet stamp it for every cast path (cascade exile-and-cast,
///   suspend's last-counter cast, foretell, jump-start, etc.). When
///   callers don't stamp, the validator treats from-zone as unspecified
///   and the restriction no-ops on that axis — matching the existing
///   posture for other from-zone-sensitive effects (Snapcaster's
///   flashback grant tracks "cast from graveyard" via a different
///   path).
/// - <b>Effect of activated abilities of cards in graveyards</b>:
///   Drannith's printed clause only touches *casting* spells; activated
///   abilities such as cycling, channel, or Dread Wanderer-style
///   recursion are out of scope and remain unrestricted.
/// </summary>
[CardName("Drannith Magistrate")]
public static class DrannithMagistrateFactory
{
    public const string CardName = "Drannith Magistrate";
    public const string Cost = "{1}{W}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Drannith Magistrate with no opponent resolver wired.
    /// Suitable for card-shape / dispatcher tests — the printed static
    /// will not register any cast-from-hand restriction.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null);

    /// <summary>
    /// Construct Drannith Magistrate with the printed-static lifecycle
    /// wired against <paramref name="eventBus"/> and the opponent set
    /// supplied by <paramref name="opponentResolver"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">Returns the set of players treated
    /// as opponents at restriction-sync time. Called when Drannith
    /// Magistrate enters the battlefield. May be null — restriction
    /// simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var magistrate = new Creature(
            CardName,
            Cost,
            Power,
            Toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        magistrate.SetOwner(owner);
        magistrate.SetController(owner);

        if (opponentResolver != null)
        {
            var lifecycle = new CastFromHandOnlyRestrictionEffect(
                source: magistrate,
                eventBus: eventBus,
                affectedPlayersResolver: opponentResolver);
            lifecycle.Attach();
        }

        return magistrate;
    }
}
