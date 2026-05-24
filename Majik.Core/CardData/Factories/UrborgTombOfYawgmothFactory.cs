using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Urborg, Tomb of Yawgmoth (Planar Chaos / reprints).
///
/// Legendary Land.
/// Oracle text: "Each land is a Swamp in addition to its other types."
///
/// ## Implementation
///
/// CR 305.7 / 613.1d — a Layer 4 subtype-adding effect. Implemented via
/// the new <see cref="AddSubtypeToPermanentsEffect"/> scoped to every
/// Land on the battlefield, additively granting
/// <see cref="CardSubtype.Swamp"/>. PR #155's
/// <see cref="EffectiveManaAbilities"/> additive-vs-replacement detection
/// then derives an extra <c>{T}: Add {B}</c> for each affected land —
/// printed mana abilities are preserved (Mountain still taps for {R},
/// Mountain under Urborg taps for {R} and {B}).
///
/// Urborg has no printed mana ability of its own: it taps for {B}
/// because its own Layer 4 effect grants itself the Swamp subtype, and
/// <see cref="EffectiveManaAbilities"/> sees a newly-acquired basic
/// subtype on a land with no printed Swamp.
///
/// The Layer 4 effect's lifecycle is event-driven via
/// <see cref="GrantLandSubtypeStaticEffect"/>: subscribe to
/// <see cref="CardMovedEvent"/>, register the
/// <see cref="AddSubtypeToPermanentsEffect"/> when Urborg enters the
/// battlefield, unregister when it leaves.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so
/// the effect is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity but no live effect — suitable for pure
/// card-shape tests.
/// </summary>
[CardName("Urborg, Tomb of Yawgmoth")]
public static class UrborgTombOfYawgmothFactory
{
    public const string CardName = "Urborg, Tomb of Yawgmoth";

    /// <summary>
    /// Creates an Urborg, Tomb of Yawgmoth with correct card identity
    /// only (no live Layer 4 effect). Suitable for factory-shape /
    /// naming tests.
    /// </summary>
    public static Land Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Urborg, Tomb of Yawgmoth. When
    /// <paramref name="effects"/> is supplied, a
    /// <see cref="GrantLandSubtypeStaticEffect"/> is attached so the
    /// Layer 4 effect registers/unregisters as Urborg enters/leaves the
    /// battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="effects"/> is
    /// null the lifecycle wiring is silently skipped (matches the
    /// shape-only overload).
    /// </summary>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Legendary Land — no printed subtypes, no printed mana abilities.
        // Self-tap for {B} arises from the Layer 4 grant kicking in on
        // Urborg itself via EffectiveManaAbilities.
        var card = new Land(
            CardName,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            // CR 305.7 — "Each land is a Swamp in addition to its other
            // types." Scope every Land (including Urborg itself);
            // additively grant {Swamp}.
            var lifecycle = new GrantLandSubtypeStaticEffect(
                card,
                effects,
                eventBus,
                scope: p => p is Land,
                subtypeToGrant: CardSubtype.Swamp);
            lifecycle.Attach();
        }

        return card;
    }
}
