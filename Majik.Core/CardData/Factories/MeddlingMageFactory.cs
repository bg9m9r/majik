using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Meddling Mage (Planeshift / various reprints,
/// {W}{U}).
///
/// Creature — Human Wizard, 2/2.
/// Oracle text:
///   "As Meddling Mage enters the battlefield, choose a nonland card name.
///    Spells with the chosen name can't be cast."
///
/// ## Implemented (v1)
/// - Creature with mana cost {W}{U}, P/T 2/2, Human + Wizard subtypes
///   and correct identity / owner / controller.
/// - <b>ETB name choice</b>: accepted as an optional
///   <paramref name="chosenName"/> parameter in
///   <see cref="Create(Player,string)"/>.  Single-arg path defaults to
///   <see cref="string.Empty"/> (no restriction) for dispatcher shape
///   tests.
/// - <b>Printed static</b> (CR 601.3): name-targeted cast restriction.
///   Wired via <see cref="MeddlingMageCastRestrictionEffect"/>: while the
///   Mage is on the battlefield, the chosen name is registered into
///   <see cref="Majik.Core.Rules.CastingRestrictions"/> via
///   <c>AddNamedCardBlock</c>, and
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects any
///   <c>CastSpellAction</c> whose card name matches. The effect detaches
///   as the Mage leaves the battlefield via
///   <see cref="Majik.Core.Events.CardMovedEvent"/> on the supplied bus.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-prompt integration</b>:
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> doesn't yet
///   declare a ChooseCardName prompt. Until that lands, callers supply the
///   name directly to the factory overload.
/// - <b>"nonland card name" validation</b>: the chosen name is accepted as
///   a raw string; enforcement that it isn't a basic land name is deferred
///   (rules-layer validation, not mechanical).
/// </summary>
public static class MeddlingMageFactory
{
    public const string CardName = "Meddling Mage";
    public const string Cost = "{W}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct a Meddling Mage with no chosen name. Suitable for
    /// card-shape / dispatcher tests — the printed static will not block
    /// any casts.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, chosenName: string.Empty, eventBus: null);

    /// <summary>
    /// Construct a Meddling Mage with <paramref name="chosenName"/> as the
    /// ETB-declared name. When <paramref name="eventBus"/> is supplied, the
    /// printed static lifecycle is fully wired (name registered into
    /// <see cref="Majik.Core.Rules.CastingRestrictions"/> while the Mage is
    /// on the battlefield; removed on LTB).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="chosenName">The nonland card name chosen as the Mage
    /// enters. An empty string means no restriction (useful for shape
    /// tests). May be null — treated as empty.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be null
    /// — the lifecycle will still sync once on Attach (no LTB
    /// unregistration).</param>
    public static Creature Create(
        Player owner,
        string? chosenName,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var mage = new Creature(
            CardName,
            Cost,
            Power,
            Toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        mage.SetOwner(owner);
        mage.SetController(owner);

        var name = chosenName ?? string.Empty;
        if (!string.IsNullOrEmpty(name))
        {
            var lifecycle = new MeddlingMageCastRestrictionEffect(
                source: mage,
                chosenName: name,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return mage;
    }
}
