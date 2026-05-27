using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Containment Construct (Modern Horizons 3,
/// {1}).
///
/// Artifact Creature — Construct 2/1. Oracle text:
///   "Whenever you discard a nonland card, you may exile it. If you
///    do, you may play that card this turn."
///
/// ## Implemented (v1)
///
/// - 2/1 Artifact Creature — Construct, mana cost {1}. The base
///   <see cref="Creature"/> shell carries the Construct subtype; the
///   Artifact card type is additively flagged via
///   <see cref="Card.AddCardType"/> (mirrors <see cref="MemniteFactory"/>
///   / <see cref="ArcboundWorkerFactory"/>).
/// - <b>Discard trigger (CR 603.1)</b>: the engine has no dedicated
///   <c>DiscardedEvent</c> (see <see cref="CuratorOfMysteriesFactory"/>
///   class doc / <see cref="NecropotenceFactory"/>); discards funnel
///   through <see cref="CardMovedEvent"/> with
///   <c>FromZone == Hand &amp;&amp; ToZone == Graveyard</c>. The trigger
///   filters that funnel to:
///     * cards owned by Containment Construct's controller ("you
///       discard"),
///     * cards that are NOT lands (printed "nonland card" gate —
///       CR 109.3 / CR 305).
/// - <b>"You may exile it" branch</b>: agent-driven via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/> tagged
///   <see cref="BotIntent.CardAdvantage"/>; declined by default for
///   tests with no agent (legacy auto-accept handled by the default
///   intent heuristic — see <see cref="IPlayerAgent.ChooseYesNoAsync"/>).
///   On accept the discarded card is moved Graveyard → Exile.
/// - <b>"If you do, you may play that card this turn" branch</b>: the
///   exiled card is stamped with
///   <see cref="Card.GrantRuntimeExileCast"/> for the discarder, cost =
///   the exiled card's printed mana cost (the runtime grant surface
///   used by Ragavan / Light Up the Stage / Igneous Inspiration). The
///   grant clears on the first <see cref="PhaseStateType.Cleanup"/>
///   <see cref="StepStartedEvent"/> seen on the supplied
///   <see cref="IEventBus"/> after the discard — that is the end of the
///   CURRENT turn (CR 514.2), matching the printed "this turn"
///   duration. Without an event bus the stamp is permanent until
///   manually cleared (test-only posture, mirrors
///   <see cref="RagavanNimblePilfererFactory"/> / Light Up the Stage).
///
/// ## Lands gate
///
/// "Nonland card" — CR 109.3 / CR 305. The trigger uses
/// <see cref="ICard.HasType"/> with <see cref="CardType.Land"/> to gate
/// the discard. A card with multiple types that includes Land (e.g. an
/// artifact land — Ancient Den, Seat of the Synod) is still a land and
/// therefore does NOT fire the trigger (CR 305.7).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Replacement-style "exile instead of graveyard"</b>: the printed
///   wording is a triggered ability ("Whenever you discard...you may
///   exile it"), not a replacement effect. v1 implements it as a
///   trigger: the discard resolves Hand → Graveyard first, then the
///   trigger fires and moves Graveyard → Exile. Observable difference
///   from a true replacement is the brief Graveyard sojourn — relevant
///   for "as it would be put into the graveyard" replacement effects
///   on the discarded card (e.g. Rest in Peace) and for
///   <see cref="CardMovedEvent"/> ordering. Both are well within the
///   v1 acceptable-shape envelope (Necropotence-style replacement is
///   reserved for the printed-replacement card; Containment Construct
///   is printed as a triggered ability).
/// - <b>Triggering off self-discards by Construct's own cycling /
///   abilities</b>: not relevant — Construct has no discard cost on
///   itself. Generic discards of the Construct card (rummage / Faithless
///   Looting) are nonland cards by Construct's controller, so they
///   fire the trigger — Construct lands in its owner's graveyard before
///   the trigger resolves, so the trigger's <c>activeZones</c> is the
///   battlefield (CR 113.6) and the trigger fires off the OTHER copy
///   of Construct in play, not the just-discarded one. With a single
///   Construct in play, discarding it does not fire its own trigger
///   (correct — the source left the battlefield before the trigger
///   could resolve).
/// </summary>
[CardName("Containment Construct")]
public static class ContainmentConstructFactory
{
    public const string CardName = "Containment Construct";
    public const string PrintedManaCost = "{1}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Containment Construct with no live event-bus / trigger
    /// wiring (the shape / dispatcher path). The discard trigger is
    /// attached but not registered, and the may-play-this-turn grant
    /// will not auto-clear at end of turn. Suitable for unit / shape
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Containment Construct with optional runtime services.
    /// When <paramref name="triggers"/> is supplied, the discard trigger
    /// is registered so <see cref="CardMovedEvent"/> publications
    /// auto-queue it. When <paramref name="eventBus"/> is supplied, the
    /// "may play this turn" grant clears on the first
    /// <see cref="PhaseStateType.Cleanup"/> step seen after the
    /// discard (CR 514.2 — end of the current turn).
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
            subtypes: new[] { CardSubtype.Construct });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types (mirrors Memnite / Arcbound Worker).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Discard trigger — CR 603.1. The engine has no dedicated
        // DiscardedEvent; discards funnel through CardMovedEvent with
        // FromZone == Hand && ToZone == Graveyard (see
        // NecropotenceFactory.NecropotenceDiscardExileReplacement). Gate
        // to the controller's discards of nonland cards. The trigger
        // captures the discarded card in a per-event closure so the
        // resolve body can see it (CR 603.2 — triggered ability is
        // associated with the specific event that triggered it).
        // ----------------------------------------------------------------
        ICard? capturedDiscard = null;

        var discardCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.FromZone != ZoneType.Hand) return false;
            if (e.ToZone != ZoneType.Graveyard) return false;
            // "Nonland card" — CR 109.3 / 305. Multi-type cards that
            // include Land (artifact lands etc.) are still lands per
            // CR 305.7 and do NOT fire this trigger.
            if (e.Card.HasType(CardType.Land)) return false;
            // "You discard" — gate to Construct's controller (CR 109.5).
            // The discarded card's owner is the discarder.
            if (!ReferenceEquals(e.Card.Owner, card.Controller ?? owner))
                return false;

            capturedDiscard = e.Card;
            return true;
        });

        var discardEffect = new Effect(
            $"{CardName}: may exile discarded card + grant may-play-this-turn",
            () =>
            {
                var discarded = capturedDiscard;
                capturedDiscard = null;
                if (discarded == null) return;

                var controller = card.Controller ?? owner;
                var agent = AgentRegistry.Get(controller);

                // "You may exile it" — CR 603.1 may-clause. Default
                // auto-accept (BotIntent.CardAdvantage) so tests with no
                // agent registered take the upside branch.
                bool exile = agent == null
                    ? true
                    : agent.ChooseYesNoAsync(
                        $"Exile {discarded.Name} discarded by {controller.Name}?",
                        BotIntent.CardAdvantage).GetAwaiter().GetResult();
                if (!exile) return;

                // Move Graveyard → Exile. Guard the zone in case a
                // sibling effect already moved the card (Rest in Peace,
                // Leyline of the Void replacement that beats this
                // trigger to the punch).
                if (discarded.Zone != ZoneType.Graveyard) return;
                var graveyardOwner = discarded.Owner;
                if (graveyardOwner == null) return;
                if (!graveyardOwner.Zones.Graveyard.GetCards().Contains(discarded))
                    return;

                graveyardOwner.Zones.Graveyard.RemoveCard(discarded);
                graveyardOwner.Zones.Exile.AddCard(discarded);
                discarded.SetZone(ZoneType.Exile);

                // "If you do, you may play that card this turn."
                // CR 118.9 — runtime grant surface used by Ragavan /
                // Light Up the Stage / Igneous Inspiration. Cost is the
                // exiled card's printed mana cost ("you may play that
                // card" with no alternate-cost rider).
                if (discarded is not Card stampable) return;
                stampable.GrantRuntimeExileCast(controller, stampable.ManaCostValue);

                if (eventBus == null) return;

                // "This turn" — CR 514.2. Clear on the first Cleanup
                // step seen after the discard (mirrors Ragavan's EOT
                // cleanup pattern). The discard can happen on any
                // player's turn, so the cleanup we wait for is the next
                // one regardless of who is active. Only revoke if the
                // stamp we set is still the live grant — a re-stamp by
                // a later effect overwrites and we must not clobber it.
                Action<StepStartedEvent>? handler = null;
                handler = (e) =>
                {
                    if (e.StepType != PhaseStateType.Cleanup) return;
                    // Only clear if still our grant (allowedCaster matches).
                    if (ReferenceEquals(stampable.RuntimeExileCastAllowedCaster, controller))
                    {
                        stampable.ClearRuntimeExileCast();
                    }
                    if (handler != null) eventBus.Unsubscribe(handler);
                };
                eventBus.Subscribe(handler);
            });

        var discardTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: discardCondition,
            effects: new IEffect[] { discardEffect },
            // CR 113.6 — abilities on permanent cards function from the
            // battlefield only. A Containment Construct in hand /
            // graveyard / exile / library does NOT fire its trigger.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(discardTrigger);
        triggers?.RegisterTriggeredAbility(discardTrigger);

        return card;
    }
}
