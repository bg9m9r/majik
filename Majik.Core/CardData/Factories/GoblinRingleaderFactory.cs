using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Ringleader (Apocalypse / Modern Horizons 2
/// reprints, {3}{R}). Creature — Goblin 2/2. Oracle text (verified against
/// Scryfall):
///   "Haste (This creature can attack and {T} as soon as it comes under your
///    control.)
///    When this creature enters, reveal the top four cards of your library.
///    Put all Goblin cards revealed this way into your hand and the rest on
///    the bottom of your library in any order."
///
/// The base shape (name, Creature, Goblin subtype, {3}{R}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>goblin-ringleader.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Haste + the ETB reveal are
/// layered on here — the JSON <c>AbilityDefinition</c> schema doesn't express
/// keyword markers or this bespoke reveal-and-take-by-subtype effect (same
/// posture as the other JSON-backed cards whose behaviour outgrows the
/// schema, e.g. <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Haste (CR 702.10)</b> — wired as a <see cref="KeywordAbility"/> marker
///   so <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> surfaces it.
///   Same shape as <see cref="GoblinChieftainFactory"/>.
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b>: self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. No intervening-if
///   (CR 603.4 does not apply — the oracle text has no "if" at trigger time).
///   On resolution:
///   1. Reveal the top four cards of the controller's library (CR 701.16 —
///      a public reveal; one <see cref="CardRevealedEvent"/> per card when an
///      event bus is supplied). Fewer than four available → reveal what's
///      there (graceful short-circuit).
///   2. Every revealed card with the Goblin subtype (CR 205.3m,
///      <c>HasSubtype(CardSubtype.Goblin)</c>) goes Library → Hand.
///   3. The rest go to the BOTTOM of the library, in revealed order
///      ("in any order" — CR 701.x; the engine bottoms them deterministically
///      top-to-bottom, which is a legal choice). <c>AddCard</c> appends =
///      bottom in the library's index-0-is-top contract (same convention as
///      <see cref="SeaGateOracleFactory"/>).
/// - Controller closure re-resolves at execute time via
///   <c>card.Controller ?? owner</c> so blink / control-change scenarios
///   reveal for the correct player (same as <see cref="CoilingOracleFactory"/>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger attached but not
///   registered with a <see cref="TriggerManager"/>. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully wired.
///   ETB trigger registered with <paramref name="triggers"/> so a
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> to the
///   battlefield routes it to the stack (CR 603.3); reveal publishes
///   <see cref="CardRevealedEvent"/> when <paramref name="eventBus"/> is given.
///
/// ## Deferred (v1 gaps)
/// - <b>Bottom-ordering choice</b>: "in any order" is resolved
///   deterministically (revealed order) rather than via an agent prompt. The
///   non-Goblins are hidden again once bottomed, so the ordering is not
///   publicly observable — a deterministic choice is rules-legal and the
///   simplest correct behaviour.
/// - <b>ETB triggers on the cards taken</b>: cards move Library → Hand via
///   raw-zone manipulation; nothing in the printed text triggers on that, so
///   no ZoneService routing is needed here (the only zone changes are to
///   hand / bottom-of-library, neither of which fires ETB / replacement
///   effects).
/// </summary>
[CardName("Goblin Ringleader")]
public static class GoblinRingleaderFactory
{
    public const string CardName = "Goblin Ringleader";
    public const string Slug = "goblin-ringleader";
    public const int RevealCount = 4;

    /// <summary>
    /// Construct Goblin Ringleader with the printed Haste keyword wired but no
    /// live bus / trigger-manager. The ETB trigger is attached for shape
    /// inspection; not registered with any <see cref="TriggerManager"/>.
    /// Suitable for dispatcher / structural tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Goblin Ringleader with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is registered
    /// so a <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> to the
    /// battlefield routes it to the stack (CR 603.3). When
    /// <paramref name="eventBus"/> is supplied, each revealed card publishes a
    /// <see cref="CardRevealedEvent"/> (CR 701.16) so portal / log subscribers
    /// can flash the reveal.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus for the public reveal. May be null —
    /// the reveal then happens silently (no event), which is acceptable for
    /// shape / unit tests.</param>
    /// <param name="triggers">Trigger manager to register the ETB ability
    /// with. May be null for shape / unit tests.</param>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Goblin
        // subtype, {3}{R}, 2/2). The JSON carries no abilities — Haste + the
        // ETB reveal are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.10 — Haste. KeywordAbility marker; CombatAbilities.HasHaste
        // reads it. Same shape as Goblin Chieftain.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, reveal the top four cards of your
        //    library. Put all Goblin cards revealed this way into your hand
        //    and the rest on the bottom of your library in any order."
        //
        // Unconditional self-ETB via Triggers.OnEnterBattlefieldSelf — no
        // intervening-if (CR 603.4 does not apply). ActiveZones =
        // { Battlefield } (CR 603.6a). Controller closure re-resolves at
        // execute time so blink / control-change reveals for the right player.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: reveal top {RevealCount}, Goblins to hand, rest to bottom",
            () =>
            {
                var controller = card.Controller ?? owner;
                var library = controller.Zones.Library;

                // Snapshot the top four (or fewer) — these are the revealed
                // cards. Take is order-preserving (index 0 = top).
                var revealed = library.GetCards().Take(RevealCount).ToList();
                if (revealed.Count == 0)
                    return; // empty library — no-op (CR 701.16, nothing to reveal)

                foreach (var seen in revealed)
                {
                    // CR 701.16 — public reveal. Publish when a bus is wired so
                    // portal / log subscribers can flash each revealed card.
                    eventBus?.Publish(new CardRevealedEvent(
                        seen, controller, ZoneType.Library, CardName));
                }

                // Partition: Goblins → hand; the rest → bottom of library.
                // Iterate the snapshot (revealed order) so the bottomed cards
                // keep a deterministic, rules-legal order ("in any order").
                foreach (var seen in revealed)
                {
                    library.RemoveCard(seen);

                    if (seen.HasSubtype(CardSubtype.Goblin))
                    {
                        // CR 205.3m — Goblin card → controller's hand.
                        controller.Zones.Hand.AddCard(seen);
                        seen.SetZone(ZoneType.Hand);
                    }
                    else
                    {
                        // Non-Goblin → bottom of library. AddCard appends =
                        // bottom in the index-0-is-top contract.
                        library.AddCard(seen);
                        seen.SetZone(ZoneType.Library);
                    }
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
