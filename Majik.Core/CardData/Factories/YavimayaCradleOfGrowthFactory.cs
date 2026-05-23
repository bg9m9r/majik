using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yavimaya, Cradle of Growth (Dominaria United).
///
/// Legendary Land.
/// Oracle text: "Each land is a Forest in addition to its other types."
///
/// ## Implementation
///
/// CR 305.7 / 613.1d — Same Layer 4 machinery as Urborg, Tomb of
/// Yawgmoth: a <see cref="GrantLandSubtypeStaticEffect"/> wraps an
/// <see cref="AddSubtypeToPermanentsEffect"/> scoped to every Land on
/// the battlefield, additively granting
/// <see cref="CardSubtype.Forest"/>. PR #155's
/// <see cref="EffectiveManaAbilities"/> additive-vs-replacement
/// detection then derives an extra <c>{T}: Add {G}</c> for each
/// affected land (printed mana abilities preserved).
///
/// Yavimaya has no printed mana ability; it taps for {G} because its
/// own Layer 4 effect grants itself the Forest subtype.
/// </summary>
public static class YavimayaCradleOfGrowthFactory
{
    public const string CardName = "Yavimaya, Cradle of Growth";

    /// <summary>
    /// Creates a Yavimaya, Cradle of Growth with correct card identity
    /// only (no live Layer 4 effect). Suitable for factory-shape /
    /// naming tests.
    /// </summary>
    public static Land Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Yavimaya, Cradle of Growth. When
    /// <paramref name="effects"/> is supplied, a
    /// <see cref="GrantLandSubtypeStaticEffect"/> is attached so the
    /// Layer 4 effect registers/unregisters as Yavimaya enters/leaves
    /// the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>.
    /// </summary>
    public static Land Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Land(
            CardName,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            // CR 305.7 — "Each land is a Forest in addition to its other
            // types." Scope every Land; additively grant {Forest}.
            var lifecycle = new GrantLandSubtypeStaticEffect(
                card,
                effects,
                eventBus,
                scope: p => p is Land,
                subtypeToGrant: CardSubtype.Forest);
            lifecycle.Attach();
        }

        return card;
    }
}
