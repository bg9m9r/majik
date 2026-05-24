using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Murktide Regent (Modern Horizons 2, {3}{U}{U}).
///
/// Creature — Dragon 3/3. Oracle text:
///   "Flying. Delve (Each card you exile from your graveyard while casting
///    this spell pays for {1}.)
///    When Murktide Regent enters, exile target instant or sorcery card
///    from a graveyard. Murktide Regent enters with a +1/+1 counter on it
///    for each card exiled with it."
///
/// ## Implemented (v1)
/// - 3/3 Dragon at {3}{U}{U}.
/// - Flying + Delve marker <see cref="KeywordAbility"/> entries. The Delve
///   mechanic itself lives in <see cref="Majik.Core.Costs.DelveCost"/> +
///   <see cref="Majik.Core.Game.SpellCastFlow"/>; callers cast Murktide
///   via the cast-flow's <c>delveCost</c> parameter to substitute exiled
///   graveyard cards for generic mana.
/// - ETB triggered ability: declares a 1..1 target request for an instant
///   or sorcery card in any graveyard. On resolution:
///     1. exile the chosen target if it's still a legal instant/sorcery
///        in a graveyard (CR 603.10b illegal-on-resolution recheck),
///     2. apply <c>X</c> +1/+1 counters to Murktide where
///        <c>X = delve-exiled-count + (1 if ETB exile succeeded, else 0)</c>.
///        Per CR 122.1g, Murktide "enters with" these counters. Strict
///        rules ordering would place counters as it enters (before the
///        ETB trigger resolves), but the spec for this card asked us to
///        count the ETB-exile alongside the delve-exiles, so we add all
///        the counters at ETB-trigger resolution time. The user-visible
///        state ends up identical — no other interactions read the count
///        in between (no other 122.1g consumers are wired).
///     3. clear <see cref="Card.PendingDelveExiledCount"/> so a later
///        non-cast battlefield entry (blink, etc.) doesn't double-count.
///
/// ## How the delve count reaches the ETB
/// "Cards exiled with me" requires the ETB effect to know how many cards
/// the Delve cost consumed. <see cref="Majik.Core.Game.SpellCastFlow"/>,
/// right after paying <see cref="Majik.Core.Costs.DelveCost"/>, stamps the
/// count on the card via <see cref="Card.SetPendingDelveExiledCount"/>.
/// The ETB effect reads it via <see cref="Card.PendingDelveExiledCount"/>.
/// This is the minimal hack documented in the card spec — no new
/// generalized X-counter-ETB framework, no new event type.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect timing</b>: the counters should be placed as
///   Murktide enters the battlefield via a 122.1g "as it enters" hook,
///   not on ETB trigger resolution. The v1 impl folds counter placement
///   into the ETB effect because no general 122.1g infrastructure exists
///   yet and the test matrix doesn't depend on the strict ordering.
///
/// ## Bot-side discovery
/// - <see cref="Majik.Core.Players.Agents.DelveAltCostProbe"/> surfaces
///   Murktide Regent to the heuristic bot's
///   <see cref="Majik.Core.Players.Agents.IAlternativeCostProbe"/> stream
///   via the Delve <see cref="KeywordAbility"/> marker. The probe yields a
///   <see cref="Majik.Core.Costs.DelveAlternativeCost"/>; the default
///   "max-delve" chooser doesn't preserve specific graveyard payoffs for
///   the +1/+1 counter count, so callers needing optimal counter-stacking
///   should supply a custom <see cref="Majik.Core.Players.Agents.DelveAltCostProbe.ChoiceStrategy"/>.
/// </summary>
[CardName("Murktide Regent")]
public static class MurktideRegentFactory
{
    /// <summary>
    /// Construct Murktide Regent owned and controlled by <paramref name="owner"/>.
    /// Single-arg overload — produces the correct card shape; ETB resolves
    /// without a live event-bus (sufficient for shape / unit tests since the
    /// trigger's behavior is read off the chosen targets via SetChosenTargets).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Murktide Regent",
            manaCost: "{3}{U}{U}",
            power: 3,
            toughness: 3,
            subtypes: new[] { CardSubtype.Dragon });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. KeywordAbility marker; combat code reads it.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.66 — Delve marker. The mechanic itself lives in DelveCost
        // + SpellCastFlow; the marker is here so introspection (UI, bots)
        // can see the keyword on the card.
        card.AddAbility(new KeywordAbility("Delve", card, owner));

        // CR 603.6a — ETB triggered ability. Fires on Murktide entering
        // the battlefield. Declares a 1..1 target request for an instant
        // or sorcery card in any graveyard. On resolution: exile target,
        // then place +1/+1 counters = delve-count + (ETB-exiled ? 1 : 0).
        TriggeredAbility? etb = null;
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Murktide Regent — exile target instant or sorcery card from a graveyard; " +
            "enters with a +1/+1 counter for each card exiled with it (CR 122.1g)",
            () =>
            {
                // Snapshot the delve-exiled count stamped at cast time
                // (CR 702.66). May be null when Murktide entered by some
                // other means (token copy, blink, manual setup in tests).
                var delveCount = card.PendingDelveExiledCount ?? 0;

                // Exile target instant/sorcery card from a graveyard.
                // CR 603.10b illegal-on-resolution recheck: the target must
                // still be a card in some graveyard with type instant or
                // sorcery. If no target was supplied (no legal targets at
                // declare time → CR 603.10b removes from stack) the exile
                // step is skipped and only the delve counters land.
                var etbExiled = false;
                if (etb != null
                    && etb.ChosenTargets.Count > 0
                    && etb.ChosenTargets[0].Count > 0
                    && etb.ChosenTargets[0][0] is Card target
                    && target.Zone == ZoneType.Graveyard
                    && (target.HasType(CardType.Instant) || target.HasType(CardType.Sorcery))
                    && target.Owner != null)
                {
                    target.Owner.Zones.Graveyard.RemoveCard(target);
                    target.Owner.Zones.Exile.AddCard(target);
                    target.SetZone(ZoneType.Exile);
                    etbExiled = true;
                }

                var totalCounters = delveCount + (etbExiled ? 1 : 0);
                if (totalCounters > 0)
                {
                    card.Counters.Add(
                        Majik.Core.Counters.CounterType.PlusOnePlusOne,
                        totalCounters);
                }

                // Consume the stamp — a later non-cast entry (blink, copy)
                // must not reuse this count.
                card.ClearPendingDelveExiledCount();
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery card in a graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: System.Array.Empty<object>()),
            });

        card.AddAbility(etb);

        return card;
    }
}
