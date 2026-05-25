using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ranger-Captain of Eos (Modern Horizons,
/// {1}{W}{W}).
///
/// Creature — Human Soldier Ranger, 3/3. Oracle text (per Scryfall):
///   "When this creature enters, you may search your library for a
///    creature card with mana value 1 or less, reveal it, put it into
///    your hand, then shuffle.
///    Sacrifice this creature: Your opponents can't cast noncreature
///    spells this turn."
///
/// ## Implemented (v1)
/// - 3/3 Human Soldier Ranger with mana cost {1}{W}{W}.
/// - <b>ETB tutor (CR 603.6a / 701.19a)</b>: When Ranger-Captain enters,
///   the controller's library is searched deterministically for the first
///   creature card with mana value &le; 1; if found, it is moved
///   Library → Hand, then the library is shuffled (CR 701.20a) via
///   <see cref="LibraryShuffle.ShuffleLibrary"/>. v1 picker is the
///   deterministic first-eligible-card pattern Stoneforge Mystic / Eldritch
///   Evolution use; agent-driven "you may" + reveal-event emission deferred
///   alongside the rest of the tutor family. CR 202.3 mana-value gate
///   reads <see cref="Card.ManaCostValue"/>.<c>TotalValue</c>, which is
///   the canonical mv accessor used elsewhere in the engine.
/// - <b>Sacrifice activated ability (CR 602 / 601.3)</b>: "Sacrifice this
///   creature: Your opponents can't cast noncreature spells this turn."
///   Single <see cref="ActivatedAbility"/> with
///   <see cref="AdditionalCost.Sacrifice"/> as the sole cost (no mana
///   pip). On resolution every player supplied by the
///   <c>opponentResolver</c> closure has a turn-scoped noncreature-spell
///   restriction registered against them via the new
///   <see cref="CastingRestrictions.AddNoncreatureSpellRestrictionForTurn"/>
///   slot (sibling of the existing turn-scoped uncounterable rider).
///   <see cref="ActionValidator.ValidateCastSpell"/> consults
///   <see cref="CastingRestrictions.CannotCastNoncreatureSpell"/> and
///   rejects any cast whose <see cref="CastSpellAction.Card"/> is not a
///   Creature. Creature spells pass through (CR 601.3 — the restriction is
///   strictly "noncreature").
/// - <b>Turn-end clearing</b>: when an <see cref="IEventBus"/> is supplied,
///   the factory subscribes a one-shot
///   <see cref="TurnEndedEvent"/> handler that calls
///   <see cref="CastingRestrictions.ClearNoncreatureRestrictionForTurn"/>
///   when the current turn ends (CR 514.2 — "this turn" effects expire at
///   cleanup). Without a bus the caller is responsible for clearing the
///   restriction (matching Veil of Summer's posture — see
///   <see cref="CastingRestrictions.ClearUncounterableForTurn"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven tutor prompt</b>: the ETB body picks the first
///   eligible creature card in the controller's library deterministically.
///   A full implementation would prompt the controller's agent
///   (CR 701.19a) for which mv-&le;-1 creature to fetch, including the
///   "you may" opt-out clause.
/// - <b>Reveal event</b>: the ETB tutor moves the card to hand without
///   emitting a <see cref="CardRevealedEvent"/>. Same gap as Stoneforge
///   Mystic's tutor.
/// - <b>Targeted noncreature restriction at choose-time</b>: the
///   restriction is bare "your opponents can't cast noncreature spells";
///   v1 covers the validator rejection but does not model agent-side
///   "spell illegal-to-attempt" — agents that probe legal moves will see
///   the rejection at validate time, but the prompt UI surfaces no
///   pre-filter (same posture as the cast-from-hand-only restriction
///   Drannith Magistrate enforces).
/// - <b>Sacrifice activation surface</b>: the sacrifice cost runs through
///   <see cref="AdditionalCost.Sacrifice"/>'s no-op
///   <see cref="AdditionalCost.Pay"/> stub (the cost type's <c>Pay</c>
///   currently TODOs the zone move); the effect body performs the actual
///   battlefield → graveyard mutation so the restriction registration
///   sees Ranger-Captain in the graveyard at restriction time. Same
///   posture as Glen Elendra Archmage's sacrifice-self counter.
/// </summary>
[CardName("Ranger-Captain of Eos")]
public static class RangerCaptainOfEosFactory
{
    public const string CardName = "Ranger-Captain of Eos";
    public const string PrintedManaCost = "{1}{W}{W}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int TutorMaxManaValue = 1;

    /// <summary>
    /// Construct Ranger-Captain of Eos with no live
    /// <see cref="TriggerManager"/> / <see cref="IEventBus"/> wiring.
    /// The ETB trigger is attached to the card shape but not registered;
    /// the sacrifice ability uses an empty opponent set (resolves to a
    /// no-op restriction registration). Suitable for card-shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Ranger-Captain of Eos with optional runtime services.
    /// When <paramref name="opponentResolver"/> is supplied, the
    /// sacrifice activated ability registers a turn-scoped
    /// noncreature-spell restriction against each opponent on resolve.
    /// When <paramref name="eventBus"/> is supplied, a one-shot
    /// <see cref="TurnEndedEvent"/> handler clears the restriction at
    /// end of turn (CR 514.2). When <paramref name="triggers"/> is
    /// supplied, the ETB tutor trigger is registered so a
    /// <see cref="CardMovedEvent"/> to the battlefield places it on the
    /// stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[]
            {
                CardSubtype.Human,
                CardSubtype.Soldier,
                CardSubtype.Ranger,
            });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / 701.19a.
        //   "When this creature enters, you may search your library for
        //    a creature card with mana value 1 or less, reveal it, put it
        //    into your hand, then shuffle."
        // v1: deterministic first-eligible-card picker (mirrors
        // StoneforgeMystic / EldritchEvolution / SilverGill posture).
        // CR 202.3 mv gate reads ManaCostValue.TotalValue.
        // CR 701.20a shuffle via LibraryShuffle.ShuffleLibrary.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor a creature card with mana value 1 or less to hand",
            () =>
            {
                var pick = owner.Zones.Library.GetCards()
                    .OfType<Card>()
                    .FirstOrDefault(c => c.HasType(CardType.Creature)
                                         && c.ManaCostValue.TotalValue <= TutorMaxManaValue);
                if (pick == null)
                {
                    // CR 701.19a — declined / no candidate is legal;
                    // shuffle still happens per printed oracle ("then
                    // shuffle" runs even on a "may" decline).
                    LibraryShuffle.ShuffleLibrary(owner, "ranger-captain-of-eos");
                    return;
                }

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                LibraryShuffle.ShuffleLibrary(owner, "ranger-captain-of-eos");
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Sacrifice activated ability — CR 602 / 601.3.
        //   "Sacrifice this creature: Your opponents can't cast
        //    noncreature spells this turn."
        // Cost: AdditionalCost.Sacrifice(self). Resolution body performs
        // the battlefield → graveyard mutation (CR 701.16) — the cost
        // type's Pay stub is a no-op today (same posture as Glen Elendra
        // Archmage's sacrifice-self counter), then registers a
        // turn-scoped restriction against each opponent in
        // CastingRestrictions.
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: sacrifice self, then opponents can't cast noncreature spells this turn",
            () =>
            {
                // ---- Sacrifice self (CR 701.16) ----
                if (card.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(card);
                    var sacOwner = card.Owner ?? owner;
                    sacOwner.Zones.Graveyard.AddCard(card);
                    card.SetZone(ZoneType.Graveyard);
                }

                // ---- Register the turn-scoped noncreature restriction ----
                if (opponentResolver == null) return;
                var opponents = opponentResolver();
                if (opponents == null) return;
                foreach (var opp in opponents)
                {
                    if (opp == null) continue;
                    CastingRestrictions.AddNoncreatureSpellRestrictionForTurn(opp);
                }
            });

        var sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Sacrifice(card) },
            effects: new IEffect[] { sacEffect });

        card.AddAbility(sacAbility);

        // ----------------------------------------------------------------
        // Turn-end cleanup — CR 514.2.
        // When an event bus is supplied, subscribe a long-lived
        // TurnEndedEvent handler that clears the turn-scoped
        // noncreature-spell restriction at the end of every turn. The
        // global registry is the single shared source of truth for the
        // rider — one subscription suffices, even if the ability is
        // activated multiple times across turns.
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnEndedEvent>(_ =>
                CastingRestrictions.ClearNoncreatureRestrictionForTurn());
        }

        return card;
    }
}
