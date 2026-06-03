using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boggart Harbinger (Lorwyn, {2}{B}).
///
/// Creature — Goblin Shaman 2/1. Oracle text (verified against Scryfall):
///   "When this creature enters, you may search your library for a Goblin
///    card, reveal it, then shuffle and put that card on top."
///
/// The base shape (name, Creature, Goblin + Shaman subtypes, {2}{B}, 2/1)
/// is materialised from the embedded JSON definition
/// (<c>boggart-harbinger.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the ETB tutor-to-top trigger
/// is layered on here (the JSON <c>AbilityDefinition</c> schema does not yet
/// carry a library-stacking tutor effect). Shape mirrors
/// <see cref="GoblinRecruiterFactory"/> with the predicate widened from
/// "Goblin creature card" to "Goblin card" and the count fixed at a single
/// (optional) pick per the printed "a Goblin card".
///
/// ## Implemented (v1)
/// - 2/1 Creature — Goblin Shaman, mana cost {2}{B}, owner/controller wired.
/// - <b>ETB triggered ability (CR 603.1, CR 603.6a)</b>: when Boggart
///   Harbinger enters the battlefield, the controller's library is scanned
///   for cards with <see cref="CardSubtype.Goblin"/> (any card type — the
///   oracle reads "a Goblin card", not "Goblin creature card"). The agent's
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> selects one
///   top-of-library pick (deterministic first-match fallback when no agent
///   is registered). The ability is a <b>"may"</b> (CR 603.5 — optional);
///   a null agent pick is a legal decline.
/// - When a pick is selected, the chosen Goblin card is moved
///   Library → Library top via <see cref="Zone.RemoveCard"/> +
///   <see cref="Zone.InsertCardAt"/> so the next draw step pulls it. Per the
///   printed sequencing "shuffle and put that card on top", the library is
///   shuffled (CR 701.20a) with the pick already extracted, then the pick is
///   re-inserted on top.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the picked card moves Library → Library (top)
///   without publishing a CardRevealedEvent; same gap as the other tutor
///   factories (Goblin Recruiter, Goblin Matron).
/// </summary>
[CardName("Boggart Harbinger")]
public static class BoggartHarbingerFactory
{
    public const string CardName = "Boggart Harbinger";
    public const string Slug = "boggart-harbinger";

    /// <summary>
    /// Construct Boggart Harbinger with no live runtime wiring. The ETB
    /// trigger is attached to the card shape but not registered with a
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Boggart Harbinger with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is registered
    /// so a <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> to
    /// the battlefield automatically queues the ability. The
    /// <paramref name="zoneService"/> + <paramref name="eventBus"/>
    /// parameters are accepted for signature parity with
    /// <see cref="GoblinRecruiterFactory"/>; the library re-order does not
    /// currently need either (Library is controller-owned and the move stays
    /// inside that zone).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = zoneService; // reserved for parity; library-internal move.
        _ = eventBus;    // reserved for parity / future reveal-event wiring.

        // Base shape from the embedded JSON definition (name, Creature,
        // Goblin + Shaman, {2}{B}, 2/1). No abilities in the JSON — the ETB
        // tutor trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1, CR 603.6a.
        //   "When this creature enters, you may search your library for a
        //    Goblin card, reveal it, then shuffle and put that card on top."
        // Predicate: card HasSubtype(Goblin) (any card type — "Goblin card",
        // not "Goblin creature card"). Optional "may" (CR 603.5): a null
        // agent pick is a legal decline. Agent picker mirrors
        // GoblinRecruiterFactory; deterministic first-match fallback when no
        // agent is registered.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: stack a Goblin card on top of library",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                var candidates = controller.Zones.Library.GetCards()
                    .Where(c => c.HasSubtype(CardSubtype.Goblin))
                    .ToList();
                if (candidates.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                ICard? pick = agent != null
                    ? (await agent.ChooseLibraryPickAsync(
                        ctx: ctx.Game,
                        candidates,
                        "Goblin card").ConfigureAwait(false))
                    : candidates[0];
                if (pick == null) return; // CR 603.5 — "you may"; decline is legal.

                // Remove the pick, shuffle the rest of the library, then put
                // the pick on top. Matches printed sequencing: "shuffle and
                // put that card on top" — the shuffle is over the remaining
                // library, with the pick already extracted.
                controller.Zones.Library.RemoveCard(pick);

                // CR 701.20a — shuffle after the search resolves, before
                // returning the pick to the top.
                LibraryShuffle.ShuffleLibrary(controller, "boggart-harbinger");

                controller.Zones.Library.InsertCardAt(0, pick);
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
