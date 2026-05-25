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
/// Named-card factory for Goblin Recruiter (Visions / many reprints,
/// {1}{R}).
///
/// Creature — Goblin 1/1. Oracle text:
///   "When Goblin Recruiter enters, search your library for any number of
///    Goblin creature cards, reveal those cards, then shuffle and put them
///    on top of your library in any order."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Goblin, mana cost {1}{R}, owner/controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b>: when Recruiter enters the
///   battlefield, the controller's library is scanned for Goblin creature
///   cards. The agent's <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///   selects one top-of-library pick (deterministic first-match fallback
///   when no agent is registered); a null pick is a legal decline
///   (CR 701.19a — "any number" includes zero).
/// - When a pick is selected, the chosen Goblin creature card is moved
///   Library → Library top via raw zone remove + <see cref="Zone.InsertCardAt"/>
///   so the next draw step pulls it. Library is then shuffled
///   (CR 701.20a) <em>before</em> re-inserting the pick on top — the
///   printed oracle reads "shuffle and put them on top of your library in
///   any order", so the shuffle happens with the pick already extracted,
///   then the pick goes back on top.
///
/// ## "Any number" — single-pick v1
/// Oracle allows any subset (zero or more) of Goblin creature cards. The
/// engine's existing <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
/// surface is single-pick (same constraint as
/// <see cref="GoblinMatronFactory"/>), so v1 ships single-pick semantics:
/// tutor at most one Goblin creature card to the top of the library. The
/// canonical Goblin Recruiter "stack the deck" line (Goblin Ringleader
/// chains, Earthcraft + Squirrel Nest style combos) needs multi-pick to
/// be fully expressive — same agent-prompt MVP gap as Scapeshift /
/// Goblin Ringleader. Zero-pick (decline) is faithfully modelled.
///
/// ## Deferred (v1 gaps)
/// - <b>Multi-pick "any number"</b>: see above. Today Recruiter tutors at
///   most one card; full subset selection needs an agent multi-pick prompt.
/// - <b>Reveal event</b>: the picked card moves Library → Library (top)
///   without publishing a CardRevealedEvent; same gap as the other tutor
///   factories.
/// - <b>Non-creature Goblin cards</b>: the predicate gates on
///   <c>HasType(Creature) AND HasSubtype(Goblin)</c> per oracle ("Goblin
///   creature cards"). Goblin-subtype non-creature cards (no such cards
///   exist in Modern, but the predicate is intentionally tight) are
///   excluded.
/// </summary>
[CardName("Goblin Recruiter")]
public static class GoblinRecruiterFactory
{
    public const string CardName = "Goblin Recruiter";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Goblin Recruiter with no live runtime wiring. The ETB
    /// trigger is attached to the card shape but not registered with a
    /// <see cref="TriggerManager"/>; the library re-order uses raw zone
    /// manipulation. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Goblin Recruiter with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB trigger is
    /// registered so a <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
    /// to the battlefield automatically queues the ability. The
    /// <paramref name="zoneService"/> + <paramref name="eventBus"/>
    /// parameters are accepted for signature parity with
    /// <see cref="GoblinMatronFactory"/> / <see cref="GoblinLackeyFactory"/>;
    /// the library re-order does not currently need either (Library is
    /// controller-owned and the move stays inside that zone).
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

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1, CR 603.6a.
        //   "When Goblin Recruiter enters, search your library for any
        //    number of Goblin creature cards, reveal those cards, then
        //    shuffle and put them on top of your library in any order."
        // Predicate: card has CardType.Creature AND CardSubtype.Goblin.
        // Agent picker mirrors GoblinMatronFactory / MysticalTutorFactory;
        // deterministic first-match fallback when no agent is registered.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Goblin Recruiter: stack a Goblin creature card on top of library",
            () =>
            {
                var controller = card.Controller ?? owner;

                var candidates = controller.Zones.Library.GetCards()
                    .Where(c => c.HasType(CardType.Creature) && c.HasSubtype(CardSubtype.Goblin))
                    .ToList();
                if (candidates.Count == 0) return;

                var agent = AgentRegistry.Get(controller);
                ICard? pick = agent != null
                    ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates,
                        "Goblin creature card")
                        .GetAwaiter().GetResult()
                    : candidates[0];
                if (pick == null) return; // CR 701.19a — "any number" includes zero.

                // Remove the pick, shuffle the rest of the library, then put
                // the pick on top. Matches printed sequencing: "shuffle and
                // put them on top of your library in any order" — the
                // shuffle is over the remaining library, not the picks.
                controller.Zones.Library.RemoveCard(pick);

                // CR 701.20a — shuffle after the search resolves, before
                // returning the pick to the top.
                Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(controller, "goblin-recruiter");

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
