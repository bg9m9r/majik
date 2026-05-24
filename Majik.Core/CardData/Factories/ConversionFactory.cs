using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conversion (Alpha / Beta / Unlimited / Revised).
///
/// Enchantment — {2}{W}{W}
/// Oracle text (original):
///   "At the beginning of your upkeep, sacrifice Conversion unless you pay {W}{W}.
///    All Mountains are Plains."
///
/// ## Implementation
///
/// The Layer 4 "All Mountains are Plains" portion is wired via the shared
/// <see cref="RetypeLandsStaticEffect"/> binder (CR 305.6 / 613.1d):
/// scope every Land whose subtype set contains <see cref="CardSubtype.Mountain"/>
/// (basic Mountains, dual lands with the Mountain subtype like Stomping
/// Ground / Sacred Foundry, and any land already retyped to Mountain by
/// Blood Moon), and retype the land-subtype slot to {Plains}. Combined
/// with PR #155's <see cref="EffectiveManaAbilities"/>, affected lands
/// lose their printed mana abilities and tap for {W}.
///
/// Note Conversion's scope is unusual relative to Blood Moon: it
/// IGNORES the basic/nonbasic distinction and keys solely on whether the
/// land has the Mountain subtype.
///
/// ## Deferred (v1 gaps)
/// - The upkeep "sacrifice unless you pay {W}{W}" portion is NOT
///   implemented. Cumulative-upkeep-style conditional sacrifice plumbing
///   is not yet present in the engine; this factory ships the Layer 4
///   type-change only. See PR body for tracking.
/// </summary>
[CardName("Conversion")]
public static class ConversionFactory
{
    public const string CardName = "Conversion";
    public const string Cost = "{2}{W}{W}";

    private static readonly IReadOnlySet<CardSubtype> PlainsOnly =
        new HashSet<CardSubtype> { CardSubtype.Plains };

    /// <summary>
    /// Creates a Conversion with correct card identity only (no live
    /// Layer 4 effect). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Conversion. When <paramref name="effects"/>
    /// is supplied, a <see cref="RetypeLandsStaticEffect"/> is attached so
    /// the Layer 4 effect registers/unregisters as Conversion enters/leaves
    /// the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. The upkeep sacrifice clause is
    /// deferred — only the type-change is live.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            // CR 305.6 — "All Mountains are Plains." Scope every Land
            // whose subtypes include Mountain (basic or nonbasic), and
            // retype to {Plains}.
            var lifecycle = new RetypeLandsStaticEffect(
                card,
                effects,
                eventBus,
                scope: p => p is Land && p.Subtypes.Contains(CardSubtype.Mountain),
                newLandSubtypes: PlainsOnly);
            lifecycle.Attach();
        }

        return card;
    }
}
