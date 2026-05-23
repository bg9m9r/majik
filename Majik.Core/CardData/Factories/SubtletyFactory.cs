using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Subtlety (Modern Horizons 2, {3}{U}).
///
/// Creature — Elemental Incarnation 3/3. Oracle text:
///   "Flash
///    When this creature enters, return target creature or planeswalker an
///    opponent controls to its owner's hand. Then that player looks at the top
///    card of their library and may put it on the bottom of their library.
///    Evoke—Exile a blue card from your hand."
///
/// ## Implemented (v1)
/// - 3/3 Elemental Incarnation with Flash + Evoke keyword markers (mirrors
///   <see cref="SolitudeFactory"/> for the
///   <see cref="NamedCardFactory"/> dispatcher path; the data-driven load
///   path attaches identical markers via <see cref="KeywordBinder"/>).
/// - Evoke alt-cost (<see cref="Majik.Core.Costs.EvokeAlternativeCost"/>):
///   exile a blue card from hand replaces the {3}{U} mana cost (CR 702.74
///   + CR 117.11).
/// - Evoke sacrifice trigger (<see cref="EvokeFactory"/>): "When this creature
///   enters, if its evoke cost was paid, sacrifice it" (CR 702.74b).
/// - ETB bounce trigger: when Subtlety enters the battlefield, fires a
///   triggered ability that returns one target permanent
///   (Creature or Planeswalker) controlled by an opponent to its owner's
///   hand (CR 701.10), then the bounced permanent's owner looks at the top
///   card of their library and decides whether to put it on the bottom
///   ("may"). The look-and-bottom decision is sourced from the bounced
///   player's registered <see cref="IPlayerAgent"/> via
///   <see cref="AgentRegistry"/> — agents implement this as a
///   <see cref="ScryAction.ScryDecision"/> over a 1-card peek (ToBottom
///   models the "yes, put on bottom" branch; TopOrder models "leave on
///   top"). When no agent is registered the peeked card stays on top
///   (pre-agent default: opponent declines the "may").
///
/// ## Deferred (v1 gaps)
/// - "Look at" private-information surfacing — the engine has no separate
///   "reveal-to-player-only" channel yet; the peek is implicit in the
///   ScryDecision call.
/// - Target re-legality at resolution: standard
///   <see cref="TriggeredAbility"/> behaviour applies — if the chosen
///   target is no longer a creature/planeswalker controlled by an opponent
///   at resolution, the bounce no-ops and the look-rider is skipped (it
///   keys off "that player", which depends on the bounce having a chosen
///   target).
/// </summary>
public static class SubtletyFactory
{
    public const string CardName = "Subtlety";
    public const string PrintedManaCost = "{3}{U}";

    /// <summary>Construct Subtlety owned and controlled by <paramref name="owner"/>.</summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 3,
            toughness: 3,
            subtypes: new[] { CardSubtype.Elemental, CardSubtype.Incarnation });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.8 (Flash), CR 702.74 (Evoke). The
        // NamedCardFactory / direct-test path doesn't run KeywordBinder,
        // so attach the markers here for parity with the data-driven load.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB bounce-and-look trigger (CR 603.6a / CR 701.10).
        // Declares a "target creature or planeswalker an opponent controls"
        // TargetRequest (exactly one). The effect reads the trigger's
        // ChosenTargets, returns the picked permanent to its owner's
        // hand, then runs a 1-card library peek for that owner with a
        // "may put on bottom" decision sourced from their registered
        // IPlayerAgent.
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Subtlety — bounce target opponent's creature/planeswalker; that player looks at top of library and may put it on bottom",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Permanent target) return;
                if (ReferenceEquals(target, card)) return;
                if (target.Zone != ZoneType.Battlefield) return; // illegal at resolution
                if (!(target.HasType(CardType.Creature) || target.HasType(CardType.Planeswalker))) return;

                var targetOwner = target.Owner;
                if (targetOwner == null) return;

                // CR 701.10 — return to owner's hand.
                var fromController = target.Controller ?? targetOwner;
                fromController.Zones.Battlefield.RemoveCard(target);
                targetOwner.Zones.Hand.AddCard(target);
                target.SetZone(ZoneType.Hand);

                // CR 701.20 / CR 121.1 — "Then that player looks at the top
                // card of their library and may put it on the bottom of
                // their library." Implemented as a 1-card scry-style peek
                // decision over `targetOwner`'s library: ToBottom = the
                // "yes, put on bottom" branch; TopOrder = "leave on top"
                // ("may" — default to top when no agent is registered).
                var peeked = ScryAction.Peek(targetOwner, 1);
                if (peeked.Count == 0) return; // empty library — nothing to look at.

                var agent = AgentRegistry.Get(targetOwner);
                ScryAction.ScryDecision decision;
                if (agent != null)
                {
                    // TODO: drop sync-over-async once IEffect.Execute becomes async.
                    decision = agent.ChooseScryDecisionAsync(null, peeked)
                        .GetAwaiter().GetResult();
                }
                else
                {
                    // Pre-agent default — decline the "may", leave on top.
                    decision = new ScryAction.ScryDecision(
                        ToBottom: Array.Empty<ICard>(),
                        TopOrder: peeked.ToList());
                }
                ScryAction.Apply(targetOwner, 1, decision);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or planeswalker an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
