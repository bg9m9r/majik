using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spreading Seas (Zendikar).
///
/// Enchantment — Aura — {1}{U}
/// Oracle text:
///   "Enchant land
///    When this Aura enters, draw a card.
///    Enchanted land is an Island and has '{T}: Add {U}'."
///
/// ## Implementation
///
/// CR 303.4 (Auras attach to permanents) + CR 305.6 / 613.1d (Layer 4
/// land retype). The retype is wired via
/// <see cref="AttachedAuraRetypeStaticEffect"/>, an aura-aware variant
/// of <see cref="RetypeLandsStaticEffect"/> whose scope predicate is
/// exactly the aura's <see cref="Permanent.AttachedTo"/> slot. Combined
/// with PR #155's <see cref="EffectiveManaAbilities"/>, the enchanted
/// land loses its printed mana abilities and gains the {T}: Add {U}
/// derived from the granted Island subtype — so the oracle text's
/// "and has '{T}: Add {U}'" clause is satisfied for free.
///
/// ETB draw: a <see cref="TriggeredAbility"/> watching
/// <see cref="CardMovedEvent"/> for this aura entering the battlefield;
/// effect draws a card for the controller.
///
/// ## Deferred (v1 gaps)
/// - <b>Cast-time targeting</b>: the spell-cast flow for Auras
///   (declare target land at cast → attach on resolution) is not yet
///   wired engine-wide. Tests manually <see cref="Permanent.AttachTo"/>
///   after putting both onto the battlefield.
/// - <b>ETB-draw via TriggerManager</b>: the trigger is attached to the
///   card's <see cref="Card.Abilities"/> collection, but a live
///   <see cref="TriggerManager"/> is required for the draw to actually
///   fire end-to-end during play.
/// </summary>
public static class SpreadingSeasFactory
{
    public const string CardName = "Spreading Seas";
    public const string Cost = "{1}{U}";

    private static readonly IReadOnlySet<CardSubtype> IslandOnly =
        new HashSet<CardSubtype> { CardSubtype.Island };

    /// <summary>
    /// Creates a Spreading Seas with correct card identity only (no live
    /// Layer 4 effect). Suitable for factory-shape / naming tests.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, effects: null, eventBus: null);

    /// <summary>
    /// Creates a fully-wired Spreading Seas. When <paramref name="effects"/>
    /// is supplied, an <see cref="AttachedAuraRetypeStaticEffect"/> is
    /// attached so the Layer 4 effect registers/unregisters as the aura
    /// enters/leaves the battlefield via <see cref="CardMovedEvent"/> on
    /// <paramref name="eventBus"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        ContinuousEffectsService? effects,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            Cost,
            supertypes: null,
            subtypes: new[] { CardSubtype.Aura });
        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            // CR 303.4 / 305.6 — while attached, the enchanted land's
            // land-subtype slot is set to { Island }. EffectiveManaAbilities
            // derives {T}: Add {U} from the new subtype (CR 305.6).
            var lifecycle = new AttachedAuraRetypeStaticEffect(
                card,
                effects,
                eventBus,
                newLandSubtypes: IslandOnly);
            lifecycle.Attach();
        }

        // ETB draw trigger: "When this Aura enters, draw a card."
        var drawCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var drawEffect = new Effect(
            "Spreading Seas — controller draws a card on ETB",
            () =>
            {
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: drawCondition,
            effects: new[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drawTrigger);

        return card;
    }
}
