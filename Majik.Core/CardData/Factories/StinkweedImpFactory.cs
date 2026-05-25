using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stinkweed Imp (Ravnica: City of Guilds, {1}{B}).
///
/// Creature — Imp 1/2 Flying. Oracle text:
///   "Flying
///    Whenever Stinkweed Imp deals combat damage to a creature, destroy
///    that creature.
///    Dredge 5 ({2}, Discard this card: ... — Dredge is the replacement
///    on draw from graveyard, see <see cref="DredgeFactory"/>.)"
///
/// ## Implemented (v1)
/// - 1/2 Creature — Imp, mana cost {1}{B}.
/// - <b>Flying</b> (CR 702.9) as a <see cref="KeywordAbility"/> marker.
/// - Combat-damage-to-a-creature trigger (CR 603.1 / CR 510) wired over
///   <see cref="CombatDamageDealtEvent"/>: gates on source matching this
///   card AND <see cref="CombatDamageDealtEvent.Target"/> being a
///   Creature (per oracle "to a creature" — player + planeswalker targets
///   do NOT fire). Resolve body destroys the damaged creature via
///   <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with reason
///   <see cref="ZoneMoveReason.Destroy"/> so Indestructible (CR 702.12)
///   and Regeneration (CR 701.15) shields apply.
/// - <b>Dredge 5</b> (CR 702.52) via <see cref="DredgeFactory.Build"/> —
///   marker keyword surfaced for shape tests; when a
///   <see cref="ReplacementBus"/> is supplied the graveyard-anchored
///   draw replacement is registered so a controller-owned draw with
///   Stinkweed Imp in graveyard prompts the agent for the dredge.
///
/// ## Posture
/// Single-arg <see cref="Create(Player)"/> path attaches all three
/// abilities (Flying marker, combat trigger, Dredge marker) without
/// live wiring — suitable for shape / dispatcher tests. The
/// (owner, triggers, replacements) overload registers the combat
/// trigger with the supplied <see cref="TriggerManager"/> and the
/// Dredge replacement with the supplied <see cref="ReplacementBus"/>.
/// </summary>
[CardName("Stinkweed Imp")]
public static class StinkweedImpFactory
{
    public const string CardName = "Stinkweed Imp";
    public const string PrintedManaCost = "{1}{B}";
    public const int Power = 1;
    public const int Toughness = 2;
    public const int DredgeValue = 5;

    /// <summary>
    /// Construct Stinkweed Imp with no runtime wiring. Card identity +
    /// ability shape only; combat trigger and Dredge replacement are
    /// attached but NOT registered with a bus.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null);

    /// <summary>
    /// Construct Stinkweed Imp with optional runtime wiring. When
    /// <paramref name="triggers"/> is supplied the combat-damage trigger
    /// is registered for bus-driven firing; when
    /// <paramref name="replacements"/> is supplied the Dredge 5 draw
    /// replacement is registered (CR 702.52).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Imp });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Marker keyword; combat code reads it.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Combat-damage-to-a-creature trigger — CR 603.1 / CR 510.
        //   "Whenever Stinkweed Imp deals combat damage to a creature,
        //    destroy that creature."
        // The closure captures the damaged creature off the event so the
        // resolve body destroys the right card. CR 603.3 evaluates the
        // condition before the ability hits the stack, so the captured
        // creature is fresh by resolution time.
        // ----------------------------------------------------------------
        Creature? capturedVictim = null;

        var destroyEffect = new Effect(
            $"{CardName}: destroy creature that took combat damage",
            () =>
            {
                var victim = capturedVictim;
                if (victim == null) return;

                // CR 608.2b illegal-on-resolution check — the damaged
                // creature must still be on the battlefield. If it left
                // (already destroyed by SBA, bounced, exiled) the
                // destroy is a clean no-op.
                if (victim.Zone != ZoneType.Battlefield) return;

                // CR 701.7 + CR 702.12 + CR 701.15 — Destroy routes
                // through ZoneMoveReason.Destroy so Indestructible and
                // Regeneration shields gate.
                Fx.MoveToGraveyard(victim, ZoneMoveReason.Destroy);
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.Target is not Creature victim) return false;
                capturedVictim = victim;
                return true;
            }),
            effects: new IEffect[] { destroyEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // CR 702.52 — Dredge 5. Keyword marker + graveyard-anchored draw
        // replacement (gated on Library.Count >= 5 + agent yes/no).
        DredgeFactory.Build(card, DredgeValue, replacements);

        return card;
    }
}
