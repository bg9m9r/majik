using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Relic of Progenitus (Shards of Alara / reprints).
///
/// Artifact — {1}. Oracle text:
///   "{T}: Target player exiles a card from their graveyard.
///    {1}, Exile Relic of Progenitus: Exile all cards from all graveyards.
///    Draw a card."
///
/// ## Implemented (v1)
/// - Artifact {1} with owner/controller wiring.
/// - <b>{T}: Target player exiles a card from their graveyard</b>:
///   cost is a tap (AdditionalCost.Tap). A 1..1 TargetRequest for
///   "target player" is declared on the ability. On resolution,
///   v1 auto-picks the first card in the target player's graveyard
///   and exiles it (Graveyard → Exile). Agent prompt deferred.
/// - <b>{1}, Exile Relic of Progenitus: Exile all cards from all
///   graveyards. Draw a card</b>:
///   cost is ManaCost {1} plus a self-exile additional cost (moves
///   the Relic from Battlefield → Exile as part of cost payment —
///   AdditionalCost.SelfExile stub; the effect closure performs
///   the zone move because the generic Pay path is a stub, mirrors
///   Mishra's Bauble / Engineered Explosives). On resolution,
///   iterate every player's graveyard reachable via
///   allPlayersResolver (falls back to controller only) and exile
///   each card. The controller then draws one card.
///
/// ## Deferred (v1 gaps)
/// - <b>Target player prompt</b>: "target player" should prompt for a
///   player. v1 auto-picks the chosen target from ChosenTargets[0][0];
///   if no chosen target is set, falls back to the controller's
///   graveyard. Full agent-prompt targeting deferred.
/// - <b>Self-exile additional cost</b>: the engine's generic
///   AdditionalCost.Sacrifice is a no-op stub; self-exile is modelled
///   the same way — the effect closure moves the Relic to Exile to
///   keep test-visible behavior correct. Remove once the additional
///   cost infrastructure performs the zone move itself.
/// </summary>
public static class RelicOfProgenitusFactory
{
    public const string CardName = "Relic of Progenitus";

    /// <summary>
    /// Construct Relic of Progenitus. The all-graveyards sweep of the second
    /// activated ability is scoped to the controller only (no allPlayersResolver).
    /// Suitable for shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Relic of Progenitus with optional cross-player graveyard
    /// access. When <paramref name="allPlayersResolver"/> is supplied, the
    /// second activated ability's "exile all cards from all graveyards"
    /// sweeps every player's graveyard in resolver order. Without it, only
    /// the controller's graveyard is swept.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var relic = new Artifact(CardName, "{1}");
        relic.SetOwner(owner);
        relic.SetController(owner);

        // ----------------------------------------------------------------
        // First ability: {T}: Target player exiles a card from their graveyard.
        //
        // CR 605 — not a mana ability (exile effect, goes on the stack).
        // Cost: tap (AdditionalCost.Tap).
        // Target: 1..1 TargetRequest "target player".
        // On resolve: auto-pick first card from target player's graveyard
        // and exile it (v1 deterministic; real agent-pick deferred).
        // ----------------------------------------------------------------
        ActivatedAbility? tapAbility = null;
        var tapEffect = new Effect(
            "Relic of Progenitus: target player exiles a card from their graveyard",
            () =>
            {
                // Resolve the target player from ChosenTargets; fall back to
                // the controller when no target was set (v1 deterministic path).
                Player? targetPlayer = null;
                if (tapAbility != null
                    && tapAbility.ChosenTargets.Count > 0
                    && tapAbility.ChosenTargets[0].Count > 0
                    && tapAbility.ChosenTargets[0][0] is Player chosenPlayer)
                {
                    targetPlayer = chosenPlayer;
                }
                else
                {
                    targetPlayer = owner;
                }

                var target = targetPlayer.Zones.Graveyard.GetCards().FirstOrDefault();
                if (target == null) return; // graveyard empty — no-op

                targetPlayer.Zones.Graveyard.RemoveCard(target);
                targetPlayer.Zones.Exile.AddCard(target);
                target.SetZone(ZoneType.Exile);
            });

        tapAbility = new ActivatedAbility(
            source: relic,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(relic),
            },
            effects: new IEffect[] { tapEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        relic.AddAbility(tapAbility);

        // ----------------------------------------------------------------
        // Second ability: {1}, Exile Relic of Progenitus:
        //   Exile all cards from all graveyards. Draw a card.
        //
        // Cost: ManaCost {1} + self-exile additional cost. The self-exile
        // zone move is performed by the effect closure because the generic
        // AdditionalCost.Pay stub is a no-op (same rationale as
        // Mishra's Bauble / Engineered Explosives).
        // ----------------------------------------------------------------
        var sweepEffect = new Effect(
            "Relic of Progenitus: exile all graveyards, draw a card",
            () =>
            {
                // Self-exile: move Relic from Battlefield → Exile.
                // Guard against double-execution (idempotent if already
                // exiled by the time this closure runs).
                if (relic.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(relic);
                    owner.Zones.Exile.AddCard(relic);
                    relic.SetZone(ZoneType.Exile);
                }

                // Exile all cards from all reachable graveyards.
                var players = allPlayersResolver?.Invoke()
                    ?? (IReadOnlyList<Player>)new[] { owner };

                foreach (var p in players)
                {
                    var graveyardCards = p.Zones.Graveyard.GetCards().ToList();
                    foreach (var card in graveyardCards)
                    {
                        p.Zones.Graveyard.RemoveCard(card);
                        p.Zones.Exile.AddCard(card);
                        card.SetZone(ZoneType.Exile);
                    }
                }

                // Draw a card for the controller.
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var sweepAbility = new ActivatedAbility(
            source: relic,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Sacrifice(relic), // models the self-exile cost; zone move in effect closure
            },
            effects: new IEffect[] { sweepEffect });

        relic.AddAbility(sweepAbility);

        return relic;
    }
}
