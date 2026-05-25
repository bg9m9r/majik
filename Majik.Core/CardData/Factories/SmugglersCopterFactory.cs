using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smuggler's Copter (Kaladesh, {2}).
///
/// Legendary Artifact — Vehicle 3/3. Oracle text:
///   "Flying"
///   "Whenever Smuggler's Copter attacks or blocks, you may draw a card.
///    If you do, discard a card."
///   "Crew 1"
///
/// ## Implementation
///
/// - Shell follows the Vehicle MVP convention (mirrors
///   <see cref="EsikasChariotFactory"/>): a <see cref="Creature"/> with
///   <see cref="CardType.Artifact"/> additively stamped (CR 301.1 / 302.1).
///   Base P/T 3/3 — <see cref="CardData.Vehicles.CrewAction"/> ships this
///   through <see cref="Majik.Core.Effects.VehicleCrewEffect"/> when crewed.
///   No Legendary supertype (Smuggler's Copter is not legendary on its
///   printed face — only the "Vehicle" subtype + Artifact supertype apply).
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker consumed
///   by <see cref="CombatAbilities.HasFlying"/>.
/// - <b>Attack-or-block loot trigger</b> (CR 508.1f, CR 509.1g, CR 603.1):
///   one <see cref="TriggeredAbility"/> whose condition matches BOTH the
///   per-attacker <see cref="CreatureAttacksEvent"/> AND the
///   <see cref="BlockersDeclaredEvent"/> when this card appears as a
///   blocker in that combat. The effect is the canonical "loot 1" —
///   <see cref="Majik.Core.Primitives.Fx.DrawCards"/> 1 then
///   <see cref="Majik.Core.Primitives.Fx.Discard"/> 1 under the controller.
/// - <b>Crew 1</b> (CR 702.122): surfaced as <see cref="CrewCost"/>; callers
///   route through <see cref="CardData.Vehicles.CrewAction.Crew"/> exactly
///   as Esika's Chariot does.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" prompt</b>: the printed text is "you may draw a card. If
///   you do, discard a card." v1 takes the loot unconditionally (matches
///   the existing closure-driven loot family — Psychic Frog, Faithless
///   Looting). Agent-driven opt-out is deferred to the broader prompt pass.
/// - <b>Discard choice</b>: <see cref="Majik.Core.Primitives.Fx.Discard"/>
///   picks the first card in hand deterministically (same gap as every
///   other looter today).
/// - <b>Crew as an activated ability</b>: kept as structural data; tests
///   call <see cref="CardData.Vehicles.CrewAction.Crew"/> directly, same
///   shape as the rest of the Vehicle MVP.
/// </summary>
[CardName("Smuggler's Copter")]
public static class SmugglersCopterFactory
{
    public const string CardName = "Smuggler's Copter";
    public const string PrintedManaCost = "{2}";
    public const int CrewCost = 1;
    public const int VehiclePower = 3;
    public const int VehicleToughness = 3;

    /// <summary>
    /// Construct Smuggler's Copter with no live wiring. The loot trigger
    /// is attached to the card shape; not registered with a trigger
    /// manager. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Smuggler's Copter with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the loot trigger is
    /// registered so bus-driven attack / block events place it on the
    /// stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: VehiclePower,
            toughness: VehicleToughness,
            subtypes: new[] { CardSubtype.Vehicle });

        // CR 301.1 / 302.1 — Smuggler's Copter is an Artifact (Vehicle).
        // Stamp the Artifact card type on top of the Creature shell so
        // HasType-based lookups see it (mirrors Esika's Chariot).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying — CR 702.9. KeywordAbility marker; combat code reads it
        // via CombatAbilities.HasFlying.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Attack-or-block loot trigger — CR 508.1f + CR 509.1g, CR 603.1.
        //   "Whenever Smuggler's Copter attacks or blocks, you may draw a
        //    card. If you do, discard a card."
        // One ability, two event shapes. Per CR 603.6c a trigger may be
        // worded with "or"; we model it as a single TriggeredAbility whose
        // condition is a disjunction over CreatureAttacksEvent /
        // BlockersDeclaredEvent that mention this card.
        // ----------------------------------------------------------------
        var lootCondition = new AttackOrBlockSelfCondition(card);

        var lootEffect = new Effect(
            $"{CardName}: draw a card, then discard a card",
            () =>
            {
                var controller = card.Controller ?? owner;
                // v1: "you may" auto-takes the loot — matches the rest of
                // the looter family (Psychic Frog, Faithless Looting).
                // Empty-library / empty-hand halts each leg cleanly.
                var drawn = Majik.Core.Primitives.Fx.DrawCards(controller, 1);
                if (drawn.Count == 0) return;
                Majik.Core.Primitives.Fx.Discard(controller, 1);
            });

        var lootTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: lootCondition,
            effects: new IEffect[] { lootEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(lootTrigger);
        triggers?.RegisterTriggeredAbility(lootTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.6c — a single trigger condition that fires on either
    /// "<source> attacks" (per-attacker <see cref="CreatureAttacksEvent"/>)
    /// OR "<source> blocks" (<see cref="BlockersDeclaredEvent"/> whose
    /// combat lists <paramref name="source"/> as a blocker). Modelled
    /// inline rather than via two <see cref="EventTriggerCondition{TEvent}"/>
    /// instances so the printed "attacks or blocks" stays a single ability
    /// (CR 603.1) and only one stack object is produced per event.
    /// </summary>
    private sealed class AttackOrBlockSelfCondition : ITriggerCondition
    {
        private readonly ICard _source;

        public AttackOrBlockSelfCondition(ICard source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public Type EventType => typeof(GameEvent);

        public bool Matches(GameEvent e, ITriggeredAbility ability)
        {
            switch (e)
            {
                case CreatureAttacksEvent attack:
                    // CR 508.1f — "Whenever ~ attacks". Per-attacker shape;
                    // fires for the declared attacker once.
                    return ReferenceEquals(attack.Attacker, _source);

                case BlockersDeclaredEvent blockers:
                    // CR 509.1g — "Whenever ~ blocks". v1: any block
                    // declaration in which this card is named a blocker
                    // satisfies the trigger. Per CR 509.1g the trigger
                    // fires when blockers are declared, not later, so
                    // hooking BlockersDeclaredEvent matches the timing.
                    return blockers.Combat.GetAllBlockers()
                        .Any(b => ReferenceEquals(b.Creature, _source));

                default:
                    return false;
            }
        }
    }
}
