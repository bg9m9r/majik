using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skullclamp (Darksteel, {1}).
///
/// Artifact — Equipment. Oracle text:
///   "Equipped creature gets +1/-1."
///   "Whenever equipped creature dies, draw two cards."
///   "Equip {1}."
///
/// ## Implementation
///
/// - <b>Static "equipped creature gets +1/-1"</b> — registered via
///   <see cref="AttachedBoostEffect"/> at Layer 7c (P/T modification, CR
///   613 Layer 7c). The effect reads the source's
///   <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping
///   transfers the boost without re-registration. The -1 toughness is
///   the entire reason Skullclamp is a card — a 1-toughness creature
///   becomes 0-toughness and dies to the SBA loop (CR 704.5f), which
///   triggers the dies clause below.
/// - <b>Dies trigger (CR 603.6c / 700.4)</b> — fires when the creature
///   currently attached to Skullclamp moves from Battlefield to
///   Graveyard. The trigger condition captures the equipped creature
///   off the source's <see cref="Permanent.AttachedTo"/> at evaluation
///   time, so re-equipping mid-turn is handled (CR 603.10 — the trigger
///   fires only if the equipped creature is the one that died). On
///   resolution, the controller draws two cards.
/// - <b>Equip {1}</b> — activated ability (CR 702.6a / 702.6d). Cost is
///   <c>{1}</c>. v1 picker is deterministic: the first creature on the
///   controller's battlefield. CR 117.1a / 307.5 sorcery-speed
///   restriction is enforced via the ActionValidator gate
///   (<c>sorcerySpeed: true</c> on the activated ability). Same shape as
///   <see cref="ColossusHammerFactory"/>'s {8} activation.
///
/// ## Lifecycle
///
/// When <paramref name="continuousEffects"/> is supplied, the +1/-1 boost
/// is registered immediately; its <c>IsActive</c> gates on Skullclamp
/// being on the battlefield AND attached to a battlefield permanent. A
/// Skullclamp that has not been equipped (or that has left the
/// battlefield) silently contributes nothing.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests. The dies trigger is attached for
/// shape inspection but is not registered with a
/// <see cref="TriggerManager"/>; callers may invoke the effect directly
/// in tests, or use the full overload for bus-driven firing.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for "creature you control" (CR 702.6b)
///   — v1 picks the first controller-side creature deterministically.
/// </summary>
[CardName("Skullclamp")]
public static class SkullclampFactory
{
    public const string CardName = "Skullclamp";
    public const string Cost = "{1}";
    public const string EquipCost = "{1}";

    /// <summary>
    /// Constructs a Skullclamp with no live continuous-effects /
    /// trigger-manager wiring (the shape / dispatcher path). The +1/-1
    /// boost is not registered against any service and the dies trigger
    /// is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs a Skullclamp. When <paramref name="continuousEffects"/>
    /// is supplied, the static +1/-1 boost (Layer 7c) is registered
    /// against it; the effect is gated on Skullclamp being on the
    /// battlefield and attached to a battlefield permanent. When
    /// <paramref name="triggers"/> is supplied, the dies trigger is
    /// registered so a <see cref="CardMovedEvent"/> from battlefield to
    /// graveyard automatically queues the ability.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Static continuous effect — "Equipped creature gets +1/-1."
        // CR 613 Layer 7c — P/T modification. Effect gates on the
        // source being on the battlefield AND attached (see
        // AttachedBoostEffect.IsActive).
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: 1, toughness: -1));
        }

        // --------------------------------------------------------------
        // Dies trigger — CR 603.6c / 700.4.
        //   "Whenever equipped creature dies, draw two cards."
        // The trigger evaluates the source's CURRENT AttachedTo at
        // event time — re-equipping mid-turn shifts which creature's
        // death matches. The trigger source is Skullclamp itself so
        // activeZones is the battlefield (Skullclamp stays attached
        // while the equipped creature dies).
        // --------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName}: equipped creature died — draw 2",
            () =>
            {
                DrawCards(owner, 2);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
            {
                if (e.FromZone != ZoneType.Battlefield) return false;
                if (e.ToZone != ZoneType.Graveyard) return false;
                // The equipped creature at the moment the death event
                // fires is the one whose death is triggering the
                // ability. ZoneService publishes CardMovedEvent AFTER
                // it sets card.Zone = Graveyard, but the trigger
                // matches on the moved card, not Skullclamp's
                // AttachedTo (CR 603.10 — last-known information).
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.Card, equipped);
            }),
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // --------------------------------------------------------------
        // Equip {1} — activated ability (CR 702.6) via the
        // EquipActivatedAbility primitive. Encapsulates the sorcery-speed
        // gate, "target creature you control" candidate-gathering, attach
        // resolution, and the Puresteel-style zero-equip CostProvider hook.
        // --------------------------------------------------------------
        var equipAbility = new EquipActivatedAbility(
            source: card,
            cost: EquipCost,
            costProvider: PuresteelPaladinFactory.ZeroEquipCostProvider);

        card.AddAbility(equipAbility);

        return card;
    }

    /// <summary>
    /// Draw <paramref name="count"/> cards for <paramref name="player"/>
    /// via raw library → hand zone moves. Empty-library halts the loop
    /// and stamps <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>
    /// so the SBA loop notes the loss condition (CR 704.5b / 120.3).
    /// Mirrors the simple-draw shape used by other shape-only factory
    /// paths.
    /// </summary>
    private static void DrawCards(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                player.MarkTriedToDrawFromEmptyLibrary();
                return;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }
}
