using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of the Guildpact (Guilds of Ravnica,
/// {G/W}{G/U}{B/G}{R/G}).
///
/// Enchantment. Oracle text:
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "Each nonland permanent you control is all colors."
///   "Lands you control are every basic land type in addition to their
///    other types."
///
/// ## Implementation
///
/// Three independent clauses, each reusing engine machinery shared with
/// earlier cards:
///
/// 1. <b>Opening-hand Leyline alt-cost</b> (CR 702.95) — a marker
///    <see cref="KeywordAbility"/>
///    (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>) so
///    the shared <see cref="OpeningHandLeylineAlternativeCost"/> subscriber
///    picks the card up from
///    <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>. Identical to
///    the rest of the Leyline cycle (analogue: Leyline of Sanctity).
///
/// 2. <b>"Lands you control are every basic land type in addition to their
///    other types."</b> (CR 305.7 / 613.1d) — five additive Layer-4
///    subtype grants (Plains / Island / Swamp / Mountain / Forest), each a
///    <see cref="GrantLandSubtypeStaticEffect"/> scoped to lands this
///    card's controller controls (analogue: Urborg / Yavimaya, which grant
///    a single basic type to <em>every</em> land; here the scope is
///    narrowed to the controller's lands and widened to all five basic
///    types). <see cref="EffectiveManaAbilities"/> then derives the
///    corresponding basic mana abilities for each affected land.
///
/// 3. <b>"Each nonland permanent you control is all colors."</b>
///    (CR 105.2c / 613.1e) — a single Layer-5
///    <see cref="SetColorsEffect"/> (all five colours) scoped to nonland
///    permanents this card's controller controls. Nonland permanents whose
///    <see cref="Permanent.ActiveEffects"/> is wired to the same
///    continuous-effects service report all five colours via
///    <see cref="Permanent.GetEffectiveColors"/>.
///
/// All three lifecycles are battlefield-gated: the Layer-4 subtype grants
/// register/unregister via <see cref="GrantLandSubtypeStaticEffect"/> on
/// <see cref="CardMovedEvent"/>, and the Layer-5 colour effect's
/// <see cref="SetColorsEffect.IsActive"/> returns false once the source
/// leaves the battlefield. Controller is resolved live (CR 613.1g) so a
/// control-change effect re-scopes both clauses on the next compute pass.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so
/// the effects attach to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity but no live effects — suitable for card-shape /
/// dispatcher tests.
/// </summary>
[CardName("Leyline of the Guildpact")]
public static class LeylineOfTheGuildpactFactory
{
    public const string CardName = "Leyline of the Guildpact";
    public const string PrintedManaCost = "{G/W}{G/U}{B/G}{R/G}";

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
    /// Creates a Leyline of the Guildpact with correct card identity only
    /// (no live continuous effects). Suitable for factory-shape / naming
    /// tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Leyline of the Guildpact. When
    /// <paramref name="effects"/> is supplied, the Layer-4 land-subtype
    /// grants and the Layer-5 all-colours effect are attached to that
    /// continuous-effects service, registering/unregistering as the Leyline
    /// enters/leaves the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="effects"/> is null
    /// the continuous-effect wiring is silently skipped (matches the
    /// shape-only overload); the opening-hand keyword marker is always
    /// added.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.95 — opening-hand Leyline marker. Always present so the
        // shared subscriber can begin-the-game-with-it regardless of
        // whether the continuous-effects service was wired.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        if (effects != null)
        {
            // CR 305.7 / 613.1d — "Lands you control are every basic land
            // type in addition to their other types." One additive Layer-4
            // grant per basic type, scoped to the controller's lands.
            // Controller is resolved live so a control-change re-scopes.
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

            // CR 105.2c / 613.1e — "Each nonland permanent you control is
            // all colors." A single Layer-5 SET-all-colours effect scoped to
            // the controller's nonland permanents. Battlefield-gated by the
            // effect's own IsActive (source must be on the battlefield).
            effects.Register(SetColorsEffect.AllColors(
                source: card,
                scope: p => !(p is Land)
                            && ReferenceEquals(p.Controller, card.Controller)));
        }

        return card;
    }
}
