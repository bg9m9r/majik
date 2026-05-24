using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Amulet of Vigor (Worldwake, {1}).
///
/// Artifact — {1}. Oracle text:
///   "Whenever a permanent enters tapped under your control, untap it."
///
/// ## Implementation
///
/// A single <see cref="TriggeredAbility"/> is attached to the card,
/// subscribing to <see cref="CardMovedEvent"/>. The condition matches when:
///   1. The destination zone is the battlefield (ETB — CR 603.6a).
///   2. The moved card is a <see cref="Permanent"/>.
///   3. The permanent's controller is this card's controller (oracle: "under
///      your control"). CR 603.2 — triggered ability source.
///   4. The permanent is tapped at the moment the event fires.
///
/// Event ordering: <see cref="Majik.Core.Services.ZoneService.MoveCard"/>
/// applies "enters tapped" replacements and calls <see cref="Permanent.Tap"/>
/// BEFORE publishing <see cref="CardMovedEvent"/>, so <see cref="Permanent.IsTapped"/>
/// is already true by the time this trigger evaluates. CR 614.6 — replacement
/// effects fully resolve before any triggered abilities see the event.
///
/// The effect untaps the permanent on resolution. A guard re-checks
/// <see cref="Permanent.IsTapped"/> at resolution time so that two copies
/// of Amulet of Vigor (each placing their own pending trigger on the stack)
/// don't both attempt to untap an already-untapped permanent — the second
/// resolution becomes a no-op rather than throwing. CR 603.7c — triggered
/// ability resolution rechecks intervening if-clauses; this matches the
/// "untap it" instruction having no effect when the permanent is already
/// untapped.
///
/// Self-ETB note: when Amulet of Vigor itself enters the battlefield
/// untapped (its printed text has no ETB-tapped clause) the IsTapped guard
/// keeps the trigger from firing on its own arrival.
///
/// ## Notes
/// - Like Up the Beanstalk / Ledger Shredder / Spreading Seas, this factory
///   does not require a live <see cref="TriggerManager"/> to construct the
///   card. Pass one to the overload to register the trigger with the bus
///   for end-to-end firing.
/// - The trigger fires for ANY permanent type — creatures, lands, artifacts,
///   enchantments, planeswalkers — as long as the controller matches and
///   it landed tapped.
/// - The permanent that entered is captured via a closure stamped by the
///   trigger condition when it matches; the resolution effect reads that
///   captured reference. The current <see cref="IEffect"/> API has no
///   event payload on Apply, so a closure is the established pattern (see
///   the inline draw effects on Up the Beanstalk / Spreading Seas).
/// </summary>
[CardName("Amulet of Vigor")]
public static class AmuletOfVigorFactory
{
    public const string CardName = "Amulet of Vigor";
    public const string Cost = "{1}";

    /// <summary>
    /// Construct Amulet of Vigor with no live trigger-manager wiring.
    /// The triggered ability is attached to the card's
    /// <see cref="Card.Abilities"/> collection so structural shape tests
    /// can observe it; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Amulet of Vigor with optional trigger-manager wiring.
    /// When <paramref name="triggers"/> is supplied, the triggered ability
    /// is registered so the bus surfaces it as pending.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Closure-captured payload: stamped by the trigger condition when
        // the event matches, read by the resolution effect. CR 603.7c —
        // the triggered ability references the specific object at
        // trigger-creation time.
        Permanent? pending = null;

        // "Whenever a permanent enters tapped under your control, untap it."
        // (CR 603.2 + CR 603.6a — ETB-trigger over CardMovedEvent.)
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (e.Card is not Permanent permanent) return false;
            if (!ReferenceEquals(permanent.Controller, owner)) return false;
            if (!permanent.IsTapped) return false;
            pending = permanent;
            return true;
        });

        var untapEffect = new Effect(
            "Amulet of Vigor — untap the permanent that entered tapped",
            () =>
            {
                var target = pending;
                pending = null;
                if (target == null) return;
                // CR 603.7c — at resolution, if the permanent is no longer
                // tapped (e.g. a second Amulet's trigger already untapped
                // it) the instruction simply has no effect rather than
                // throwing. Mirrors "untap target permanent" with an
                // illegal/already-met target.
                if (target.IsTapped)
                {
                    target.Untap();
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { untapEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
