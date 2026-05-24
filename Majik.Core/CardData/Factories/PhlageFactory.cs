using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phlage, Titan of Fire's Fury (Modern Horizons 3,
/// {2}{R}{W}).
///
/// Legendary Creature — Elemental Incarnation 4/4. Oracle text:
///   "When Phlage, Titan of Fire's Fury enters, it deals 3 damage to any
///    target and you gain 3 life.
///    Escape—{2}{R}{W}, Exile three other cards from your graveyard."
///
/// ## Implemented (v1)
/// - 4/4 Legendary Creature — Elemental Incarnation, mana cost {2}{R}{W}.
/// - <b>ETB triggered ability (CR 603.6a / CR 119)</b>: declares a 1..1
///   "any target" <see cref="TargetRequest"/>; on resolution deals 3 damage
///   to the chosen target (Player / Creature / Planeswalker via
///   <see cref="SearingBlazeFactory.DealDamageWithPlaneswalker"/> — loyalty
///   removal for PWs per CR 306.7) and the controller gains 3 life
///   (CR 119.3) as part of the same resolution. Same shape as Lightning
///   Helix's resolve, lifted into a triggered ability on a creature ETB.
///
/// ## Deferred (v1 gaps)
/// - <b>Escape (CR 702.143)</b>: cast-from-graveyard alt cost with the
///   "exile three other cards from your graveyard" rider. Engine has
///   <see cref="Costs.CastFromExileAlternativeCost"/> for cast-from-exile
///   only; no graveyard variant + multi-card-exile additional-cost
///   primitive yet. Same gap as Uro, Titan of Nature's Wrath (see
///   <see cref="UroTitanFactory"/>). Once Escape ships, Phlage's ETB
///   trigger body is unchanged — escape only changes how the spell is
///   cast, not the on-resolution effect.
/// </summary>
[CardName("Phlage, Titan of Fire's Fury")]
public static class PhlageFactory
{
    public const string CardName = "Phlage, Titan of Fire's Fury";
    public const string PrintedManaCost = "{2}{R}{W}";

    public const int DamageAmount = 3;
    public const int LifeGainAmount = 3;

    /// <summary>
    /// Construct Phlage owned and controlled by <paramref name="owner"/>.
    /// Card shape + ETB triggered ability. The ETB trigger uses a 1..1
    /// "any target" <see cref="TargetRequest"/>; the caller must populate
    /// <see cref="TriggeredAbility.ChosenTargets"/> before the trigger
    /// resolves (same pattern as Solitude's ETB exile trigger).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 4,
            toughness: 4,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Incarnation });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a + CR 119 + CR 119.3.
        //   "When Phlage enters, it deals 3 damage to any target and you
        //    gain 3 life."
        // Single 1..1 "any target" TargetRequest; on resolution deals 3
        // damage to the chosen target and the controller gains 3 life as
        // part of the same resolution (CR 608.2c-style printed-order:
        // damage first, then lifegain).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var effect = new Effect(
            $"{CardName}: 3 damage to any target + 3 life",
            () =>
            {
                // CR 119.3 — controller gains 3 life unconditionally as
                // part of this resolution. Snapshot the controller at
                // resolution time (the source's current controller, which
                // is the trigger's controller).
                var controller = card.Controller ?? owner;

                // CR 608.2b — illegal target at resolution: damage clause
                // does nothing; the lifegain clause IS part of the same
                // resolution and is NOT a separate target. Phlage's only
                // target is the damage target — if it's illegal at
                // resolution the whole ability does nothing per the
                // single-target rule (CR 608.2b last clause). Treat
                // missing/illegal target as full-spell-fizzle for parity
                // with Lightning Helix.
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                var target = chosen[0][0];

                // CR 119 — damage. Routes Player / Creature / Planeswalker
                // via the shared helper so PW targets see loyalty removal
                // (CR 306.7).
                SearingBlazeFactory.DealDamageWithPlaneswalker(target, DamageAmount);

                // CR 119.3 — lifegain after damage in printed order.
                controller.GainLife(LifeGainAmount);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { effect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
