using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sythis, Harvest's Hand (Modern Horizons 2 / Theros
/// Beyond Death — {G}{W}).
///
/// Legendary Enchantment Creature — Nymph 1/2. Oracle text:
///   "Constellation — Whenever an enchantment enters under your control, you
///    gain 1 life and draw a card."
///
/// ## Implementation
///
/// Sythis is an Enchantment Creature (CR 205.2a) — both Creature AND
/// Enchantment card types via
/// <see cref="Majik.Core.Cards.PermanentBuilders.EnchantmentCreature"/>.
///
/// Constellation (CR 702.144) is a trigger-templating keyword: "Whenever an
/// enchantment you control or an enchantment you both own and control enters,
/// ..." The trigger fires whenever ANY enchantment enters under the
/// controller's control — Auras, plain Enchantments, other Enchantment
/// Creatures, AND Sythis itself (since Sythis carries the Enchantment card
/// type, its own entry satisfies the predicate, matching real-rules
/// constellation self-triggering).
///
/// Shape mirrors <see cref="PuresteelPaladinFactory"/> (Equipment-ETB → draw):
/// one <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>,
/// gated on:
///   * <c>e.ToZone == ZoneType.Battlefield</c>
///   * <c>e.Card.HasType(CardType.Enchantment)</c> — covers plain
///     enchantments AND Auras (Auras carry the Enchantment card type plus
///     the Aura subtype per CR 303.1).
///   * <c>ReferenceEquals(e.Card.Controller, owner)</c> — "under YOUR
///     control".
///
/// Effect: gain 1 life (CR 119) + draw a card (top of controller's library
/// → hand, matching the inline pattern used by
/// <see cref="UpTheBeanstalkFactory"/> and <see cref="PuresteelPaladinFactory"/>).
///
/// ## Notes
/// - Sythis is an Enchantment Creature, so its own entry satisfies the
///   <c>CardType.Enchantment</c> predicate (real-rules constellation
///   self-trigger).
/// - The trigger fires for the controller's own enchantment plays AND for
///   any other move that lands an enchantment under their control (e.g.
///   reanimation, blink). Opponent enchantments do not qualify.
/// - The single-arg dispatcher path attaches the trigger to the card shape
///   without TriggerManager wiring; use the (owner, triggers) overload for
///   end-to-end bus firing.
/// </summary>
[CardName("Sythis, Harvest's Hand")]
public static class SythisHarvestsHandFactory
{
    public const string CardName = "Sythis, Harvest's Hand";
    public const string Cost = "{G}{W}";

    /// <summary>
    /// Construct Sythis, Harvest's Hand with no live trigger-manager wiring.
    /// The constellation trigger is attached to the card's
    /// <see cref="Card.Abilities"/> collection so structural shape tests can
    /// observe it; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Sythis, Harvest's Hand with optional trigger-manager wiring.
    /// When <paramref name="triggers"/> is supplied, the constellation
    /// trigger is registered so the bus surfaces it as pending.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // CR 205.2a — Legendary Enchantment Creature: Creature + Enchantment
        // card types (so Sythis counts toward "enchantments you control" and
        // its own constellation never depends on that, but Sterling Grove /
        // Sanctum Weaver style counts pick it up).
        var card = PermanentBuilders.EnchantmentCreature(
            name: CardName,
            manaCost: Cost,
            power: 1,
            toughness: 2,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Nymph });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Constellation trigger — "Whenever an enchantment enters under
        // your control, you gain 1 life and draw a card." (CR 702.144 /
        // 603.1)
        // ----------------------------------------------------------------
        var constellationCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Enchantment)
            && ReferenceEquals(e.Card.Controller, owner));

        var constellationEffect = new Effect(
            "Sythis, Harvest's Hand — gain 1 life and draw a card on enchantment ETB",
            () =>
            {
                // Gain 1 life (CR 119).
                owner.GainLife(1);

                // Draw a card — top of controller's library → hand
                // (CR 121). Mirrors UpTheBeanstalkFactory.DrawOne /
                // PuresteelPaladinFactory's inline draw.
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var constellationTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: constellationCondition,
            effects: new IEffect[] { constellationEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(constellationTrigger);
        triggers?.RegisterTriggeredAbility(constellationTrigger);

        return card;
    }
}
