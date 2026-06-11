using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Leyline of Lightning (Modern Horizons 3,
/// {2}{R}{R}).
///
/// Enchantment. Oracle text (verified against Scryfall + the embedded seed):
///   "If this card is in your opening hand, you may begin the game with
///    it on the battlefield."
///   "Whenever you cast a spell, you may pay {1}. If you do, this enchantment
///    deals 1 damage to target player or planeswalker."
///
/// (NB: the SHIPPED oracle is "whenever you cast a spell" / "target player or
/// planeswalker" — NOT a "first spell each turn" / "any target" variant. The
/// embedded seed matches current Scryfall, which is the rules authority.)
///
/// ## Implemented
/// - Enchantment shape with mana cost {2}{R}{R}, owner / controller wired.
/// - <b>Opening-hand alt-cost</b> (CR 702.95) — marker
///   <see cref="KeywordAbility"/>
///   (<see cref="OpeningHandLeylineAlternativeCost.LeylineKeyword"/>)
///   so the shared subscriber picks Lightning up from
///   <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>.
/// - <b>"Whenever you cast a spell, you may pay {1}..."</b> (CR 603.1 / 603.3)
///   — a <see cref="SpellCastEvent"/> trigger gated to the controller's own
///   casts (CR 109.5 — "you cast"). Same on-cast attachment point as
///   <see cref="LedgerShredderFactory"/> / Prowess, minus the per-turn count
///   (this card fires on EVERY cast, not the first/second of the turn). On
///   resolution the optional {1} is paid via <see cref="Player.PayMana"/>
///   (atomic — deducts only when affordable; v1 auto-pays when able, same
///   posture as <see cref="NihilSpellbombFactory"/>'s "may pay {B}"); if paid,
///   1 damage is dealt to the chosen "target player or planeswalker" via
///   <see cref="Fx.DealDamageAny(object,int)"/> (Player → life loss,
///   Planeswalker → loyalty removal, CR 306.7). A 1..1 target request is
///   attached for shape parity; agents populate
///   <see cref="TriggeredAbility.ChosenTargets"/> before resolution (same as
///   <see cref="SwordOfFireAndIceFactory"/>'s any-target damage). No chosen
///   target → the damage is a no-op even after paying (CR 608.2b).
/// </summary>
[CardName("Leyline of Lightning")]
public static class LeylineOfLightningFactory
{
    public const string CardName = "Leyline of Lightning";
    public const string PrintedManaCost = "{2}{R}{R}";

    /// <summary>
    /// Constructs Leyline of Lightning with no live runtime wiring (the
    /// shape / dispatcher path). The cast trigger is attached to the card for
    /// shape observability but not registered with a
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Constructs Leyline of Lightning. When <paramref name="triggers"/> is
    /// supplied the on-cast trigger is registered so a controller-cast
    /// <see cref="SpellCastEvent"/> surfaces it as pending.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.95 — Leyline keyword marker.
        card.AddAbility(new KeywordAbility(
            OpeningHandLeylineAlternativeCost.LeylineKeyword, card, owner));

        // ----------------------------------------------------------------
        // "Whenever you cast a spell, you may pay {1}. If you do, this
        //  enchantment deals 1 damage to target player or planeswalker."
        // (CR 603.1 / 603.3). Fires on every SpellCastEvent whose spell is
        // controlled by Leyline's controller (CR 109.5 — "you cast").
        // ----------------------------------------------------------------
        TriggeredAbility? trigger = null;

        var damageEffect = new Effect(
            $"{CardName}: you may pay {{1}}; if you do, deal 1 damage to target player or planeswalker",
            () =>
            {
                // "You may pay {1}." v1 auto-pays when the controller's pool
                // can cover it. Player.PayMana is atomic — it returns false
                // and deducts nothing when {1} isn't available.
                var controller = card.Controller ?? owner;
                if (!controller.PayMana(ManaCost.Zero.AddGenericCost(1))) return;

                // "If you do, deal 1 damage to target player or planeswalker."
                // No chosen target → no-op (CR 608.2b — do as much as
                // possible). DealDamageAny routes Planeswalker → loyalty
                // removal (CR 306.7).
                if (trigger != null
                    && trigger.ChosenTargets.Count > 0
                    && trigger.ChosenTargets[0].Count > 0)
                {
                    Fx.DealDamageAny(trigger.ChosenTargets[0][0], 1);
                }
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<SpellCastEvent>((e, _) =>
                ReferenceEquals(e.Spell.Controller, card.Controller ?? owner)),
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
