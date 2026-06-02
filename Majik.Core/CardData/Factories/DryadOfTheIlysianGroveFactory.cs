using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dryad of the Ilysian Grove (Theros Beyond Death,
/// {2}{G}).
///
/// Type line: <c>Enchantment Creature — Nymph Dryad</c> (2/4).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "You may play an additional land on each of your turns."
///   "Lands you control are every basic land type in addition to their
///    other types."
///
/// ## Implementation
///
/// Two independent clauses:
///
/// 1. <b>"You may play an additional land on each of your turns."</b>
///    (CR 305.2 / 720) — a controller-scoped, battlefield-gated static that
///    raises the controller's per-turn land-play cap by 1. Modeled as
///    <see cref="Permanent.AdditionalLandPlaysGranted"/> = 1, summed live by
///    <see cref="Majik.Core.Game.LandDropTracker"/> over the active player's
///    permanents (same surface as Azusa / Exploration — see
///    <see cref="AzusaLostButSeekingFactory"/>). It appears on ETB, lifts on
///    LTB, stacks additively, and is correct every turn with no
///    re-application. Stamped on the single-argument <see cref="Create(Player)"/>
///    path so it is live in real matches (GameFacade's instance-swap rebuild
///    calls this overload).
///
/// 2. <b>"Lands you control are every basic land type in addition to their
///    other types."</b> (CR 305.7 / 613.1d) — five additive Layer-4 subtype
///    grants (Plains / Island / Swamp / Mountain / Forest), each a
///    <see cref="GrantLandSubtypeStaticEffect"/> scoped to lands this card's
///    controller controls. Identical machinery to
///    <see cref="LeylineOfTheGuildpactFactory"/>'s land clause. This wiring
///    requires a live <see cref="ContinuousEffectsService"/> + bus and is
///    attached only by the effects-aware
///    <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/>
///    overload.
///
/// ## Prod-wiring residual (matches Leyline of the Guildpact's posture)
///
/// GameFacade's instance-swap rebuild for a non-land named-factory permanent
/// calls only the single-argument <see cref="Create(Player)"/> overload, so
/// the Layer-4 basic-land-type grant (clause 2) is not auto-wired in a live
/// match today — exactly the same gap that already applies to Leyline of the
/// Guildpact and the lord-effect creature factories (their effects-aware
/// overloads are likewise test-only on the current rebuild path). The
/// land-play static (clause 1, the gameplay-significant half that closes the
/// SKIP) IS live in prod because it rides the single-argument path.
/// </summary>
[CardName("Dryad of the Ilysian Grove")]
public static class DryadOfTheIlysianGroveFactory
{
    public const string CardName = "Dryad of the Ilysian Grove";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 2;
    public const int Toughness = 4;

    /// <summary>CR 720 — Dryad grants one additional land play each turn.</summary>
    public const int AdditionalLandPlays = 1;

    /// <summary>Basic land types granted to the controller's lands —
    /// CR 305.6 / 205.3i "every basic land type".</summary>
    private static readonly CardSubtype[] BasicLandTypes =
    {
        CardSubtype.Plains,
        CardSubtype.Island,
        CardSubtype.Swamp,
        CardSubtype.Mountain,
        CardSubtype.Forest,
    };

    /// <summary>
    /// Construct Dryad with the land-play static wired but without the
    /// Layer-4 basic-land-type grant (no continuous-effects service).
    /// Suitable for shape / dispatcher tests and the prod instance-swap
    /// rebuild path.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Construct a fully-wired Dryad. The land-play static is always stamped;
    /// when <paramref name="effects"/> is supplied, the five Layer-4
    /// basic-land-type grants are attached, registering/unregistering as the
    /// Dryad enters/leaves the battlefield via <see cref="CardMovedEvent"/>
    /// on <paramref name="eventBus"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: null,
            subtypes: new[] { CardSubtype.Nymph, CardSubtype.Dryad });

        // CR 301.1 / 302.1 — additive Enchantment type (Enchantment Creature).
        card.AddCardType(CardType.Enchantment);

        card.SetOwner(owner);
        card.SetController(owner);

        // Clause 1 — CR 305.2 / 720 — "You may play an additional land on each
        // of your turns." Battlefield-gated, controller-scoped, summed live by
        // LandDropTracker.AdditionalLandPlaysFromBattlefield.
        card.AdditionalLandPlaysGranted = AdditionalLandPlays;

        // Clause 2 — CR 305.7 / 613.1d — "Lands you control are every basic
        // land type in addition to their other types." One additive Layer-4
        // grant per basic type, scoped to the controller's lands. Controller
        // is resolved live so a control-change re-scopes.
        if (effects != null)
        {
            foreach (var subtype in BasicLandTypes)
            {
                var landGrant = new GrantLandSubtypeStaticEffect(
                    card,
                    effects,
                    eventBus,
                    scope: p => p is Land && ReferenceEquals(p.Controller, card.Controller),
                    subtypeToGrant: subtype);
                landGrant.Attach();
            }
        }

        return card;
    }
}
