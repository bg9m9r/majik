using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Targeting;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goldspan Dragon (Kaldheim, {3}{R}{R}).
///
/// Creature — Dragon 4/4. Oracle text (Scryfall-verified):
///   "Flying, haste
///    Whenever this creature attacks or becomes the target of a spell, create
///    a Treasure token.
///    Treasures you control have '{T}, Sacrifice this artifact: Add two mana
///    of any one color.'"
///
/// ## Implemented
/// - 4/4 Creature — Dragon, mana cost {3}{R}{R}, Flying + Haste keyword
///   markers (CR 702.9 / 702.10).
/// - <b>Treasure-mana-modify continuous static</b> (the
///   <c>treasure-mana-ability-modify-static</c> deferral). Goldspan's
///   "Treasures you control have '… Add TWO mana …'" is a continuous effect
///   that modifies how the OTHER Treasure tokens its controller controls
///   produce mana (CR 611.2). Rather than re-binding every Treasure's mana
///   ability whenever Goldspan enters / leaves, this rides the existing mana
///   path: a Treasure's per-colour mana ability uses a dynamic
///   <c>Func&lt;ManaCost&gt;</c> generator that, at activation time
///   (CR 605.1 — mana abilities resolve immediately), consults
///   <see cref="TreasureManaModifierStaticAbility.ManaMultiplierFor"/> for the
///   producing player and multiplies the printed ONE pip accordingly. Goldspan
///   attaches a <see cref="TreasureManaModifierStaticAbility"/> marker (×2)
///   onto itself; the effect is live exactly while Goldspan is on the
///   battlefield (CR 604.2 — <see cref="TreasureManaModifierStaticAbility.IsActive"/>
///   keys on its source's zone), so a Treasure tapped after Goldspan dies
///   produces its base one mana again. Two Goldspans do NOT stack to four —
///   the printed value is a fixed "two" (CR 613.2; identical-shape effects
///   overwrite rather than compound).
/// - <b>Attack / becomes-target Treasure trigger</b> (CR 508.1f / 603.6c /
///   115.6). A single conceptual trigger with two trigger events; modelled as
///   TWO <see cref="TriggeredAbility"/> instances sharing one Treasure-
///   creating effect (one over <see cref="CreatureAttacksEvent"/> for this
///   card, one over <see cref="TargetsChosenEvent"/> filtered to a SPELL whose
///   chosen targets include this card). The target half fires on a spell of
///   ANY player (the printed text has no "you control" rider). Each is
///   attached via <c>AddAbility</c> so the live
///   <see cref="Majik.Core.Abilities.TriggerManager.BindCard"/> auto-registers
///   them on ETB in a real match (no multi-arg prod overload needed).
///
/// ## Notes
/// Goldspan is NOT a Modern-legal card, so it is not in the embedded seed —
/// the card is built directly via <c>new Creature</c> (no JSON resource, no
/// golden-parity-digest surface). Its value here is the engine mechanic + the
/// reusable <see cref="TreasureManaModifierStaticAbility"/> seam.
/// </summary>
[CardName("Goldspan Dragon")]
public static class GoldspanDragonFactory
{
    public const string CardName = "Goldspan Dragon";

    /// <summary>
    /// Construct Goldspan Dragon with no live wiring (shape / dispatcher path).
    /// Flying, Haste, the Treasure-mana-modify static, and the two Treasure
    /// triggers are all attached to the card so structural assertions and the
    /// pull-side static query work; nothing is registered with a manager and
    /// the Treasure creation uses a raw battlefield add with no event bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Goldspan Dragon with optional live wiring. When
    /// <paramref name="triggers"/> is supplied the attack + becomes-target
    /// triggers are registered so a <see cref="CreatureAttacksEvent"/> /
    /// <see cref="TargetsChosenEvent"/> queues the Treasure-creating ability;
    /// when <paramref name="zoneService"/> is supplied the created Treasure's
    /// ETB publishes a <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: "{3}{R}{R}",
            power: 4,
            toughness: 4,
            subtypes: new[] { CardSubtype.Dragon });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 / 702.10 — Flying + Haste keyword markers.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // "Treasures you control have '{T}, Sacrifice this artifact: Add two
        //  mana of any one color.'" (CR 611.2 continuous mana-modify static.)
        // Pull-side marker: every Treasure's dynamic mana generator consults
        // TreasureManaModifierStaticAbility.ManaMultiplierFor at activation
        // time. Attaching the marker to Goldspan is the whole wiring — no
        // push onto each Treasure, no register/unregister on token creation.
        // ----------------------------------------------------------------
        card.AddAbility(new TreasureManaModifierStaticAbility(card, owner, manaMultiplier: 2));

        // ----------------------------------------------------------------
        // "Whenever this creature attacks or becomes the target of a spell,
        //  create a Treasure token." (CR 508.1f / 603.6c / 115.6.)
        // ----------------------------------------------------------------
        var createTreasure = new Effect(
            $"{CardName}: create a Treasure token (attack / becomes-target trigger)",
            () =>
            {
                var controller = card.Controller ?? owner;
                TokenFactory.CreateTreasure(controller, zoneService);
            });

        // Attack half — OnAttackSelf (CR 508.1f).
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { createTreasure },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // Becomes-the-target-of-a-SPELL half (CR 115.6 — spells only, not
        // abilities; any player's spell, no "you control" rider).
        var targetCondition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            if (e.StackObject is not ISpell) return false;

            return e.Targets.Any(t =>
                (t.TargetType == TargetType.Permanent || t.TargetType == TargetType.Card)
                && t is Target concrete
                && ReferenceEquals(concrete.TargetObject, card));
        });

        var targetTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: targetCondition,
            effects: new IEffect[] { createTreasure },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(targetTrigger);
        triggers?.RegisterTriggeredAbility(targetTrigger);

        return card;
    }
}
