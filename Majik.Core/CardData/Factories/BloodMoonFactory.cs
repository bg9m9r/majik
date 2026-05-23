using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blood Moon (The Dark / Modern reprint).
///
/// Enchantment — {2}{R}
/// Oracle text: "Nonbasic lands are Mountains."
///
/// ## Implementation
///
/// CR 305.6 / 613.1d — a Layer 4 type-changing effect. Implemented via
/// PR #151's <see cref="SetSubtypesEffect"/> scoped to every nonbasic
/// Land on the battlefield, replacing the land-subtype category with
/// {Mountain}. PR #155's <see cref="EffectiveManaAbilities"/> then derives
/// {T}: Add {R} for each affected land (the printed mana abilities are
/// lost per CR 305.6).
///
/// The Layer 4 effect's lifecycle is event-driven via
/// <see cref="BloodMoonStaticEffect"/>: subscribe to <see cref="CardMovedEvent"/>,
/// register the <see cref="SetSubtypesEffect"/> when Blood Moon enters the
/// battlefield, unregister when it leaves. This mirrors the pattern used by
/// <see cref="TorporOrbFactory"/> + <see cref="TorporOrbStaticEffect"/>.
///
/// Callers wiring real gameplay should use
/// <see cref="Create(Player, ContinuousEffectsService, IEventBus?)"/> so the
/// effect is attached to the game's continuous-effects service. The
/// single-argument <see cref="Create(Player)"/> overload produces a card
/// with correct identity but no live effect — suitable for pure card-shape
/// tests.
/// </summary>
public static class BloodMoonFactory
{
    public const string CardName = "Blood Moon";
    public const string Cost = "{2}{R}";

    /// <summary>
    /// Creates a Blood Moon with correct card identity only (no live
    /// Layer 4 effect). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Blood Moon. When <paramref name="effects"/> is
    /// supplied, a <see cref="BloodMoonStaticEffect"/> is attached so the
    /// Layer 4 effect registers/unregisters as Blood Moon enters/leaves the
    /// battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>. When <paramref name="effects"/> is null
    /// the lifecycle wiring is silently skipped (matches the
    /// shape-only overload).
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
            var lifecycle = new BloodMoonStaticEffect(card, effects, eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
