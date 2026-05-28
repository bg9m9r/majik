using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fblthp, the Lost (War of the Spark, {1}{U}).
///
/// Legendary Creature — Homunculus 1/1. Oracle text:
///   "When Fblthp enters, draw a card. If it entered from your library
///    or was cast from your library, draw two cards instead."
///   "When Fblthp becomes the target of a spell, shuffle Fblthp into
///    its owner's library."
///
/// ## Implementation
///
/// - 1/1 Legendary Homunculus with mana cost {1}{U}, MV 2. Color identity
///   blue (derived from the {U} pip, CR 202.2c). Legendary supertype
///   (CR 205.4a) stored as a card supertype — the legend rule (CR 704.5j)
///   is enforced by the SBA engine, not this factory.
///
/// - <b>ETB triggered ability — draw 1 (or 2) (CR 603.1, CR 603.6a)</b>:
///   "When Fblthp enters, draw a card."
///   The "from library" bonus is implemented via an intervening-if check at
///   resolve time (NOT an intervening-if that gates trigger queuing, per
///   CR 603.4 — the condition is checked at resolution, not at trigger time).
///   - Draw 2 if <see cref="Card.WasCastFromLibrary"/> OR
///     <see cref="Card.WasPlacedFromLibrary"/> (set by ZoneService on a
///     Library → Battlefield move without a cast marker).
///   - Draw 1 otherwise (cast from hand, reanimation, blink, token copy, etc.).
///   Both paths route through <see cref="Fx.DrawCards"/> so the replacement
///   bus (Alms Collector, future draw replacements) and empty-library SBA
///   flag (CR 704.5b) fire correctly per CR 121.1.
///
/// - <b>"Becomes the target of a spell" triggered ability (CR 603.6c)</b>:
///   Unlike <see cref="PhantasmalBearFactory"/>'s "spell or ability" posture,
///   Fblthp's oracle says "spell" only — the predicate gates on
///   <c>e.StackObject is ISpell</c> (CR 115.6 — spells and abilities are
///   distinct stack objects; only ISpell qualifies here).
///   Effect: move Fblthp from the battlefield to its owner's library, then
///   shuffle that library (CR 701.20a). Uses a bare zone-move
///   (Battlefield → Library via the owner's zone collections + SetZone) and
///   <see cref="Majik.Core.Zones.LibraryShuffle.ShuffleLibrary"/> for the
///   mandatory shuffle. Idempotent: if Fblthp has already left the
///   battlefield when the trigger resolves (destroyed by the targeting spell,
///   blinked out, etc.) the zone-move is a no-op (CR 603.7c).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (ETB + target triggers
///   attached to the card; not registered with a TriggerManager).
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. ETB trigger is registered for CardMovedEvents; target trigger
///   is registered for TargetsChosenEvents.
///
/// ## v1 deferred
/// - The "entered from library" clause (WasPlacedFromLibrary) covers
///   direct Library → Battlefield entries (Glimpse-style effects). This
///   does NOT yet handle replacement effects that re-route the destination
///   mid-move (e.g., "if it would enter from your library, exile it
///   instead" interactions). Deferred: Containment Priest already handles
///   these via the replacement bus.
/// - No agent interaction for the "shuffle Fblthp into its owner's library"
///   choice — it is a mandatory triggered ability (no "may"), so no prompt
///   is needed.
/// </summary>
[CardName("Fblthp, the Lost")]
public static class FblthpTheLostFactory
{
    public const string CardName = "Fblthp, the Lost";
    public const string PrintedManaCost = "{1}{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Fblthp, the Lost with no live wiring. Both triggered
    /// abilities are attached to the card for shape inspection but are
    /// NOT registered with any <see cref="TriggerManager"/>. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Fblthp, the Lost with optional event-bus / trigger-manager
    /// wiring. When <paramref name="triggers"/> is supplied both triggered
    /// abilities are registered so:
    /// <list type="bullet">
    ///   <item><description>
    ///     A <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
    ///     published on the bus routes the ETB draw trigger to the stack.
    ///   </description></item>
    ///   <item><description>
    ///     A <see cref="Majik.Core.Domain.DomainEvents.TargetsChosenEvent"/>
    ///     referencing Fblthp as a target of a spell surfaces the shuffle
    ///     trigger as pending.
    ///   </description></item>
    /// </list>
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Legendary supertype (CR 205.4a) passed at construction time via
        // the Creature(supertypes: ...) overload. The legend rule (CR 704.5j)
        // is enforced by the SBA engine on each state-based action pass.
        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Homunculus });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When Fblthp enters, draw a card. If it entered from your
        //    library or was cast from your library, draw two cards instead."
        //
        // The condition is evaluated at RESOLVE time (not trigger time) per
        // CR 603.4 — Fblthp has no "if [condition]" intervening-if clause
        // that would gate trigger queuing. Instead, the resolve-body
        // branches on WasCastFromLibrary || WasPlacedFromLibrary.
        //
        // Active zone is Battlefield (CR 603.6a — ETB triggers require the
        // source to be on the battlefield at the time of trigger; the card
        // is already on the battlefield when the CardMovedEvent fires from
        // ZoneService, matching standard ETB-trigger wiring).
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: draw a card (2 if entered/cast from library)",
            () =>
            {
                var controller = card.Controller ?? owner;
                // CR 603.7c — if Fblthp is no longer on the battlefield
                // (was destroyed/bounced before the trigger resolved) the
                // draw still fires — ETB triggers use the stack-object's
                // last-known zone (Battlefield at trigger time). The draw
                // is correct regardless of current zone.
                var fromLibrary = card.WasCastFromLibrary || card.WasPlacedFromLibrary;
                Fx.DrawCards(controller, fromLibrary ? 2 : 1);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // "Becomes the target of a spell" triggered ability (CR 603.6c).
        //   "When Fblthp becomes the target of a spell, shuffle Fblthp
        //    into its owner's library."
        //
        // Condition: a TargetsChosenEvent where:
        //   1. The stack object is an ISpell (CR 115.6 — "spell" excludes
        //      activated/triggered abilities; unlike Phantasmal Bear's
        //      "spell or ability" posture).
        //   2. At least one chosen target references this Fblthp card
        //      (Permanent or Card target type, same shape as PhantasmalBear).
        //
        // Effect: move Fblthp from the battlefield to its owner's library,
        // then shuffle that library (CR 701.20a — "shuffle Fblthp into its
        // owner's library" requires both the zone-move AND the shuffle).
        // Idempotent: if Fblthp has already left the battlefield the zone-
        // move block is skipped (CR 603.7c).
        // ----------------------------------------------------------------
        var targetCondition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            // Only fire on spells, not abilities (CR 115.6).
            if (e.StackObject is not ISpell) return false;

            return e.Targets.Any(t =>
                (t.TargetType == TargetType.Permanent || t.TargetType == TargetType.Card)
                && t is Target concrete
                && ReferenceEquals(concrete.TargetObject, card));
        });

        var shuffleEffect = new Effect(
            $"{CardName}: shuffle self into owner's library",
            () =>
            {
                // CR 603.7c — idempotent: if Fblthp is already gone from
                // the battlefield (the targeting spell resolved and destroyed
                // it before this trigger resolved, or another trigger moved
                // it), the shuffle is still performed on its current zone
                // but the zone-move is skipped.
                var ownerPlayer = card.Owner ?? owner;

                if (card.Zone == ZoneType.Battlefield)
                {
                    ownerPlayer.Zones.Battlefield.RemoveCard(card);
                    ownerPlayer.Zones.Library.AddCard(card);
                    card.SetZone(ZoneType.Library);
                }

                // CR 701.20a — "shuffle Fblthp into its owner's library"
                // requires an explicit shuffle after re-insertion.
                Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(ownerPlayer, "fblthp-the-lost-target");
            });

        var targetTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: targetCondition,
            effects: new IEffect[] { shuffleEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(targetTrigger);
        triggers?.RegisterTriggeredAbility(targetTrigger);

        // eventBus retained for parity with other live-wiring overloads.
        _ = eventBus;

        return card;
    }
}
