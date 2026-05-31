using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Matron (Urza's Legacy, {2}{R}).
///
/// Creature — Goblin 1/1. Oracle text:
///   "When Goblin Matron enters, you may search your library for a Goblin
///    card, reveal that card, and put it into your hand. Then shuffle."
///
/// ## Why it gets its own factory
/// Mirrors <see cref="StoneforgeMysticFactory"/>'s ETB tutor shape but
/// filters by <see cref="CardSubtype.Goblin"/> rather than by card type
/// (Equipment / Artifact). The shared <see cref="SpellTemplates.Templates.Search.SearchSpellFactory.SearchLibrarySpell"/>
/// primitive only exposes a type-keyed predicate ladder ("creature",
/// "artifact", "instant", ...), so a Goblin-subtype tutor doesn't fit
/// that closure without expanding it. Rather than thread a subtype
/// predicate through the shared closure for a single ETB, this factory
/// hosts the bespoke tutor effect inline — same approach as
/// <see cref="StoneforgeMysticFactory"/>'s ETB Equipment search.
///
/// ## Implemented (v1)
/// - Creature {2}{R} 1/1 — Goblin subtype, owner/controller wired.
/// - <b>ETB tutor (CR 603.1, CR 701.19a)</b>: When Goblin Matron enters,
///   the controller's library is searched for a card with the Goblin
///   subtype; if found, it is moved Library → Hand. The search consults
///   the controller's <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///   (same pattern as <see cref="SpellTemplates.Templates.Search.SearchSpellFactory"/>
///   and <see cref="MysticalTutorFactory"/>) with a deterministic
///   first-match fallback when no agent is registered. A null pick
///   resolves as a legal decline (CR 701.19a — "you may"). Empty
///   candidate list = no-op.
/// - The single-arg dispatcher path produces the correct card shape
///   without TriggerManager registration; use the
///   (owner, zoneService, eventBus, triggers) overload for fully-wired
///   behaviour (parity with <see cref="StoneforgeMysticFactory"/> and
///   <see cref="GoblinLackeyFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. The picked card moves Library → Hand without
///   publishing a CardRevealedEvent; same gap as the other tutor
///   factories.
/// - <b>"You may" decline path</b> at the agent level: today only an
///   agent that returns null can decline. A "should I even search?"
///   prompt is queued behind the same may-prompt MVP that Aether Vial /
///   Goblin Lackey are waiting on.
/// </summary>
[CardName("Goblin Matron")]
public static class GoblinMatronFactory
{
    public const string CardName = "Goblin Matron";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>
    /// Construct Goblin Matron with no live runtime wiring. The ETB
    /// trigger is attached to the card shape but not registered with a
    /// <see cref="TriggerManager"/>; the library → hand move uses raw
    /// zone manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Goblin Matron with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is
    /// registered so a <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
    /// to the battlefield automatically queues the ability. The
    /// <paramref name="zoneService"/> + <paramref name="eventBus"/>
    /// parameters are accepted for signature parity with
    /// <see cref="GoblinLackeyFactory"/> / <see cref="StoneforgeMysticFactory"/>;
    /// the library → hand move does not currently need either
    /// (Library and Hand are both controller-owned, so no cross-zone
    /// ETB triggers fire on the tutored card).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = zoneService; // reserved for parity; library→hand is a non-ETB move.
        _ = eventBus;    // reserved for parity / future reveal-event wiring.

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Goblin });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1, CR 701.19a.
        //   "When Goblin Matron enters, you may search your library for a
        //    Goblin card, reveal that card, and put it into your hand.
        //    Then shuffle."
        // Predicate gates on CardSubtype.Goblin (CR 205.3 — subtype is a
        // separate axis from card type). Agent-driven pick mirrors
        // SearchSpellFactory / MysticalTutorFactory; deterministic
        // first-match fallback when no agent is registered.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Goblin Matron: tutor a Goblin card to hand",
            async ctx =>
            {
                var candidates = owner.Zones.Library.GetCards()
                    .Where(c => c.HasSubtype(CardSubtype.Goblin))
                    .ToList();
                if (candidates.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(owner);
                ICard? pick = agent != null
                    ? (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                        candidates,
                        "Goblin card").ConfigureAwait(false))
                    : candidates[0];
                if (pick == null) return; // CR 701.19a — decline is legal.

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                // CR 701.20a — shuffle after the search resolves.
                Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(owner, "goblin-matron");
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
