using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Containment Priest (Commander 2014 / Modern
/// Horizons 2, {1}{W}).
///
/// Creature — Human Cleric, 2/2.
/// Oracle text:
///   "Flash
///    If a nontoken creature would enter the battlefield and it wasn't
///    cast, exile it instead."
///
/// ## Implemented (v1)
/// - Creature with mana cost {1}{W}, P/T 2/2, Human + Cleric subtypes
///   and correct identity / owner / controller.
/// - <b>Flash</b> (CR 702.8): keyword marker wired via
///   <see cref="KeywordAbility"/>; <see cref="Majik.Core.Rules.TimingRules.CanCastAtInstantSpeed"/>
///   reads this keyword.
/// - <b>Printed replacement effect</b> (CR 614): exile non-cast, non-token
///   creatures that would enter the battlefield. Wired via
///   <see cref="ContainmentPriestExileReplacementEffect"/>: while the Priest
///   is on the battlefield, a <see cref="IReplacementEffect{ZoneMoveIntent}"/>
///   is registered on the supplied <see cref="ReplacementBus"/> that rewrites
///   destination = Exile for any <see cref="Majik.Core.Effects.ZoneMoveIntent"/>
///   where ToZone = Battlefield, Card is a Creature, card is not a token,
///   and <see cref="Majik.Core.Effects.ZoneMoveIntent.WasCast"/> = false.
///
/// ## Deferred (v1 gaps)
/// - <b>ZoneService ReplacementBus plumbing</b>: ZoneService's
///   <c>MoveCard</c> path does not yet consult the ReplacementBus for
///   most move calls, so the exile replacement is structural only in v1.
///   When ZoneService is wired to push every battlefield-entry through
///   the bus, the effect will fire automatically for Sneak Attack /
///   Through the Breach / Animate Dead / Aether Vial / etc.
/// - <b>WasCast stamping coverage</b>:
///   <see cref="Majik.Core.Effects.ZoneMoveIntent.WasCast"/> is false on
///   all intents not constructed explicitly with <c>WasCast: true</c>.
///   SpellCastFlow should stamp it once it builds its ZoneMoveIntent.
/// </summary>
public static class ContainmentPriestFactory
{
    public const string CardName = "Containment Priest";
    public const string Cost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Containment Priest with no replacement-bus wired.
    /// Suitable for card-shape / dispatcher tests — Flash keyword is
    /// present but the printed replacement will not be registered.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacementBus: null, eventBus: null);

    /// <summary>
    /// Construct Containment Priest with the printed-replacement lifecycle
    /// wired against <paramref name="replacementBus"/> and
    /// <paramref name="eventBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register the exile-replacement on. May be null — the replacement
    /// simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be null
    /// — the lifecycle will still sync once on Attach.</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacementBus,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var priest = new Creature(
            CardName,
            Cost,
            Power,
            Toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        priest.SetOwner(owner);
        priest.SetController(owner);

        // Flash — CR 702.8. Allows casting at instant speed.
        // TimingRules.CanCastAtInstantSpeed checks for this keyword.
        priest.AddAbility(new KeywordAbility("Flash", priest, owner));

        if (replacementBus != null)
        {
            var lifecycle = new ContainmentPriestExileReplacementEffect(
                source: priest,
                replacementBus: replacementBus,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return priest;
    }
}
