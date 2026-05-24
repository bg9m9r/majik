using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Manabarbs (Sixth Edition, {2}{R}{R}).
///
/// Enchantment — {2}{R}{R}. Oracle text:
///   "Whenever a player taps a land for mana, Manabarbs deals 1 damage
///    to that player."
///
/// ## Implementation
///
/// A single <see cref="TriggeredAbility"/> is attached to the card,
/// subscribing to <see cref="ManaAbilityActivatedEvent"/> (published by
/// <see cref="Services.ManaAbilityActivator"/> after the activator's mana
/// pool is topped up — CR 605). The condition matches when:
///   1. The mana ability's source is a <see cref="Land"/> (printed-type
///      check via <see cref="Card.HasType(CardType)"/>, so subtype-grant
///      effects that turn a non-land into a Land — Mishra-style — would
///      currently miss; the v1 scope is "the card is a Land permanent").
///   2. The activator is a known <see cref="Player"/>.
/// The trigger is symmetric: it fires for any player, including
/// Manabarbs's controller (oracle: "a player", not "an opponent").
///
/// The effect calls <see cref="Player.LoseLife"/> with 1 on the activator.
/// This mirrors the established v1 non-combat-damage-to-a-player path
/// used by Dark Confidant, Yawgmoth, etc. — a full
/// <see cref="Events.DamageDealtEvent"/> over the bus is not yet plumbed
/// for ability damage, so subscribers that care about damage prevention
/// would not see Manabarbs's 1; same scope decision as DarkConfidantFactory.
///
/// Non-land mana abilities (Mox Opal, Black Lotus, Cabal Ritual on its
/// own — though that's a spell, not an ability) do NOT trigger Manabarbs
/// — the printed text gates on land. Mana abilities of non-land
/// permanents (e.g. Mox Opal's WUBRG abilities) publish the same
/// <see cref="ManaAbilityActivatedEvent"/> but the source predicate
/// rejects them.
///
/// ## Notes
/// - Like Up the Beanstalk / Amulet of Vigor, this factory does not
///   require a live <see cref="TriggerManager"/> to construct the card.
///   Pass one to the overload to register the trigger with the bus for
///   end-to-end firing.
/// - The activator is captured via a closure stamped by the trigger
///   condition when it matches; the resolution effect reads that
///   captured reference. Same pattern as Amulet of Vigor's pending
///   permanent.
/// </summary>
[CardName("Manabarbs")]
public static class ManabarbsFactory
{
    public const string CardName = "Manabarbs";
    public const string Cost = "{2}{R}{R}";

    /// <summary>
    /// Construct Manabarbs with no live trigger-manager wiring. The
    /// triggered ability is attached to the card's
    /// <see cref="Card.Abilities"/> collection so structural shape tests
    /// can observe it; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Manabarbs with optional trigger-manager wiring. When
    /// <paramref name="triggers"/> is supplied, the triggered ability is
    /// registered so the bus surfaces it as pending.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Closure-captured payload: stamped by the trigger condition when
        // the event matches, read by the resolution effect. CR 603.7c —
        // the triggered ability references the specific object at
        // trigger-creation time.
        Player? pendingActivator = null;

        // "Whenever a player taps a land for mana, Manabarbs deals 1
        // damage to that player." (CR 605 — mana abilities; CR 603.2 —
        // triggered abilities over events.)
        var condition = new EventTriggerCondition<ManaAbilityActivatedEvent>((e, _) =>
        {
            // Gate on Land — the ability's Source is the tapped permanent.
            if (e.Source is not Card srcCard) return false;
            if (!srcCard.HasType(CardType.Land)) return false;
            pendingActivator = e.Player;
            return true;
        });

        var damageEffect = new Effect(
            "Manabarbs — deal 1 damage to the player who tapped the land",
            () =>
            {
                var target = pendingActivator;
                pendingActivator = null;
                target?.LoseLife(1);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
