using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sea Gate Oracle (Rise of the Eldrazi / various
/// reprints, {2}{U}).
///
/// Creature — Human Wizard 1/3. Oracle text:
///   "When this creature enters, look at the top two cards of your library.
///    Put one of them into your hand and the other on the bottom of your
///    library."
///
/// ## Implemented (v1)
/// - Card identity: {2}{U} 1/3 Creature — Human Wizard.
/// - <b>ETB triggered ability (CR 603.6a)</b>: on entering the battlefield
///   the controller looks at the top two cards of their library, puts one
///   into their hand, and the other on the bottom of their library.
/// - <b>Agent-driven selection</b>: when an <see cref="IPlayerAgent"/> is
///   registered via <see cref="AgentRegistry"/>, the agent chooses which
///   of the two peeked cards goes to hand via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (intent:
///   CardAdvantage). The other card goes to the bottom of the library.
/// - <b>Deterministic fallback</b> (no agent registered): first peeked
///   card (the top) goes to hand; second goes to the bottom.
/// - <b>Graceful short-circuit</b>: if the library has fewer than 2 cards,
///   take what's available — 0 cards = no-op, 1 card = that card to hand.
/// - Single-arg dispatcher path attaches the ETB trigger but does not
///   register it with a <see cref="TriggerManager"/>; the
///   (owner, eventBus, triggers) overload registers the trigger so a
///   <see cref="CardMovedEvent"/> to the battlefield fires it automatically
///   (CR 603.3).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal / look event</b>: the peeked cards are not broadcast via
///   CardRevealedEvent (hidden look, not a public reveal — acceptable).
/// </summary>
[CardName("Sea Gate Oracle")]
public static class SeaGateOracleFactory
{
    public const string CardName = "Sea Gate Oracle";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Sea Gate Oracle with no live bus / trigger-manager wiring.
    /// The ETB trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Sea Gate Oracle with optional event bus and trigger manager.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a <see cref="CardMovedEvent"/> to the battlefield
    /// automatically places it on the stack (CR 603.3).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus — accepted for API symmetry; unused
    /// in the v1 ETB effect body.</param>
    /// <param name="triggers">Trigger manager to register the ETB ability
    /// with. May be null for shape / unit tests.</param>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, look at the top two cards of your
        //    library. Put one of them into your hand and the other on the
        //    bottom of your library."
        //
        // Steps:
        //  1. Peek the top 2 (or fewer) cards of the controller's library.
        //  2. If no cards, no-op.
        //  3. If only 1 card, move that card to hand (graceful short-circuit).
        //  4. Otherwise, let the registered agent pick which card goes to
        //     hand (CardAdvantage intent); fall back to the top card when
        //     no agent is registered.
        //  5. Move the chosen card Library → Hand; move the unchosen card to
        //     the BOTTOM of the library (AddCard appends = bottom in FIFO
        //     order, matching the library's index-0-is-top contract).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: look at top 2, put one to hand, other to bottom",
            async ctx =>
            {
                var library = owner.Zones.Library.GetCards().Take(2).ToList();

                if (library.Count == 0)
                    return; // Empty library — no-op (graceful, no forced draw).

                if (library.Count == 1)
                {
                    // Exactly one card available — it goes to hand.
                    var only = library[0];
                    owner.Zones.Library.RemoveCard(only);
                    owner.Zones.Hand.AddCard(only);
                    only.SetZone(ZoneType.Hand);
                    return;
                }

                // Two cards available: let the agent (or fallback) choose.
                var agent = ctx.Agent ?? AgentRegistry.Get(owner);
                ICard? chosen;
                if (agent != null)
                {
                    // TODO: remove sync-over-async once IEffect.Execute becomes async.
                    chosen = (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                        candidates: library,
                        kindLabel: "card",
                        ct: default).ConfigureAwait(false));
                }
                else
                {
                    // Deterministic fallback: pick the top card (index 0).
                    chosen = library[0];
                }

                // Ensure the agent returned something valid; fall back to
                // the top card if the agent returned null or an out-of-set card.
                if (chosen == null || !library.Contains(chosen))
                    chosen = library[0];

                var other = library.First(c => !ReferenceEquals(c, chosen));

                // Move chosen → Hand.
                owner.Zones.Library.RemoveCard(chosen);
                owner.Zones.Hand.AddCard(chosen);
                chosen.SetZone(ZoneType.Hand);

                // Move other → bottom of library (AddCard appends = bottom).
                owner.Zones.Library.RemoveCard(other);
                owner.Zones.Library.AddCard(other);   // re-append = bottom
                other.SetZone(ZoneType.Library);
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
