using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phantasmal Bear (Magic 2012, {U}).
///
/// Creature — Bear Illusion 2/2. Oracle text:
///   "When this creature becomes the target of a spell or ability,
///    sacrifice it."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Bear Illusion with mana cost {U}, owner / controller
///   wired.
/// - <b>Targeted-by-spell-or-ability self-sacrifice trigger</b>
///   (CR 603.6c, CR 115.6) — fires on
///   <see cref="TargetsChosenEvent"/> whenever any chosen target across
///   the event's target list references this Phantasmal Bear. Both
///   spells AND activated/triggered abilities (e.g. an opponent's
///   <c>{T}: Target creature gets …</c> activation) trigger this — same
///   posture as <see cref="PhantasmalImageFactory"/>, NOT the spell-only
///   posture <see cref="BonecrusherGiantFactory"/> takes.
/// - On resolution the bear is sacrificed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Sacrifice"/> so the binder bypasses
///   Indestructible / regeneration gates (CR 701.16, CR 702.12b,
///   CR 701.15c). Idempotent: a second trigger resolving against an
///   already-graveyard bear is a no-op (CR 603.7c).
///
/// ## Wiring
/// - <see cref="Create(Player)"/> attaches the trigger to the card shape
///   without bus-driven registration. Suitable for dispatcher / shape
///   tests.
/// - <see cref="Create(Player, IEventBus, TriggerManager)"/> additionally
///   registers the trigger with the live <see cref="TriggerManager"/>
///   so a <see cref="TargetsChosenEvent"/> places it on the stack
///   automatically (mirrors <see cref="PhantasmalImageFactory"/>'s live
///   wiring overload).
///
/// ## Comparison with Phantasmal Image
/// Phantasmal Image is the {1}{U} 0/0 Illusion with the same sacrifice
/// rider PLUS an enters-as-copy-of-any-creature replacement
/// (CR 706.10). Phantasmal Bear is the strict-floor printed-statline
/// cousin: same self-sac rider, no copy effect. The trigger shape is
/// identical so this factory borrows Phantasmal Image's condition
/// predicate verbatim.
/// </summary>
[CardName("Phantasmal Bear")]
public static class PhantasmalBearFactory
{
    public const string CardName = "Phantasmal Bear";
    public const string PrintedManaCost = "{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Phantasmal Bear with the self-sac trigger attached to
    /// the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Phantasmal Bear with optional event-bus / trigger
    /// manager wiring. When <paramref name="triggers"/> is supplied the
    /// self-sac trigger is registered so a
    /// <see cref="TargetsChosenEvent"/> referencing this bear surfaces
    /// it as pending automatically.
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
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Bear, CardSubtype.Illusion });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Targeted-by-spell-or-ability self-sacrifice trigger — CR
        // 603.6c, CR 115.6.
        //   "When this creature becomes the target of a spell or ability,
        //    sacrifice it."
        //
        // Same predicate shape as PhantasmalImageFactory — match on any
        // chosen target referencing this card. Permanent + Card target
        // types both count (CR 115.4 — spells/abilities target permanents
        // on the battlefield; Card covers grave/exile-zone targeting for
        // symmetry with Bonecrusher / Phantasmal Image).
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            return e.Targets.Any(t =>
                (t.TargetType == TargetType.Permanent || t.TargetType == TargetType.Card)
                && t is Target concrete
                && ReferenceEquals(concrete.TargetObject, card));
        });

        var sacEffect = new Effect(
            $"{CardName}: sacrifice it",
            () =>
            {
                // CR 603.7c — if Phantasmal Bear has already left the
                // battlefield (another trigger this turn already
                // sacrificed it, removal moved it, etc.) the sacrifice
                // is a no-op.
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 701.16 — sacrifice bypasses Indestructible
                // (CR 702.12b) / regeneration (CR 701.15c). Pass the
                // Sacrifice reason so OracleSpellBinder doesn't gate.
                OracleSpellBinder.MoveToGraveyard(
                    card, Majik.Core.Zones.ZoneMoveReason.Sacrifice);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { sacEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        // Live registration with the TriggerManager so the bus surfaces
        // the trigger as pending when a spell or ability targets this
        // bear.
        triggers?.RegisterTriggeredAbility(trigger);

        // eventBus retained in the signature for parity with other live-
        // wiring overloads (Bonecrusher / Phantasmal Image / Dress Down).
        // No direct publish from this factory today — sacrifice is
        // modelled as a raw zone move via OracleSpellBinder.
        _ = eventBus;

        return card;
    }
}
