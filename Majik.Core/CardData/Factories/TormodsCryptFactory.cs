using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tormod's Crypt (Ice Age + many reprints).
///
/// Artifact — {0}. Oracle text:
///   "{T}, Sacrifice Tormod's Crypt: Exile all cards from target player's
///    graveyard."
///
/// ## Implemented (v1)
/// - Artifact {0} with owner/controller wiring.
/// - Single activated ability with two costs:
///     * <see cref="AdditionalCost.Tap"/> — the {T} half of the cost.
///     * <see cref="AdditionalCost.Sacrifice"/> — the self-sac half. The
///       generic <c>AdditionalCost.Pay</c> path is a no-op for sacrifice
///       today (TODO comment on the Sacrifice case), so the resolution
///       closure performs the Battlefield → Graveyard move itself to keep
///       the test-visible behaviour correct. This mirrors the
///       <see cref="RelicOfProgenitusFactory"/> self-exile model.
/// - 1..1 "target player" <see cref="TargetRequest"/>.
/// - Resolution: read the chosen target from <c>ChosenTargets</c>, then
///   iterate every card in that player's graveyard and move it to that
///   player's exile zone via <see cref="ZoneManager"/>. CR 608.2b — empty
///   graveyard target is a clean no-op.
///
/// ## Deferred (v1 gaps)
/// - <b>Target player prompt</b>: like Relic of Progenitus, v1 reads the
///   chosen target from <c>ChosenTargets[0][0]</c>; if no target is set,
///   falls back to the controller's graveyard. Full agent-prompt targeting
///   is deferred.
/// - <b>Self-sacrifice additional cost</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice path is a TODO no-op; the
///   self-sac zone move is modelled inside the effect closure (same
///   rationale as Relic of Progenitus' self-exile). Remove the in-closure
///   move once <see cref="AdditionalCost"/> performs the zone change itself.
/// </summary>
[CardName("Tormod's Crypt")]
public static class TormodsCryptFactory
{
    public const string CardName = "Tormod's Crypt";

    /// <summary>
    /// Construct Tormod's Crypt. The activated ability's target-player
    /// graveyard exile is scoped via <c>ChosenTargets[0][0]</c>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var crypt = new Artifact(CardName, "{0}");
        crypt.SetOwner(owner);
        crypt.SetController(owner);

        // ----------------------------------------------------------------
        // {T}, Sacrifice Tormod's Crypt: Exile all cards from target
        // player's graveyard.
        //
        // CR 605 — not a mana ability (exile effect, goes on the stack).
        // Cost: tap + self-sac (the self-sac zone move is performed by
        // the effect closure because AdditionalCost.Sacrifice is a no-op
        // TODO).
        // Target: 1..1 TargetRequest "target player".
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;
        var exileEffect = new Effect(
            "Tormod's Crypt: exile all cards from target player's graveyard",
            () =>
            {
                // Self-sacrifice: move Tormod's Crypt from Battlefield →
                // Graveyard. Guard against double-execution.
                if (crypt.Zone == ZoneType.Battlefield)
                {
                    owner.Zones.Battlefield.RemoveCard(crypt);
                    owner.Zones.Graveyard.AddCard(crypt);
                    crypt.SetZone(ZoneType.Graveyard);
                }

                // Resolve the target player from ChosenTargets; fall back
                // to the controller when no target was set (v1 deterministic
                // path mirrors Relic of Progenitus).
                Player? targetPlayer = null;
                if (ability != null
                    && ability.ChosenTargets.Count > 0
                    && ability.ChosenTargets[0].Count > 0
                    && ability.ChosenTargets[0][0] is Player chosenPlayer)
                {
                    targetPlayer = chosenPlayer;
                }
                else
                {
                    targetPlayer = owner;
                }

                // Snapshot the graveyard before mutating it. CR 608.2b —
                // an empty graveyard is a clean no-op (the ability still
                // resolves, no cards move).
                var graveyardCards = targetPlayer.Zones.Graveyard.GetCards().ToList();
                foreach (var card in graveyardCards)
                {
                    targetPlayer.Zones.Graveyard.RemoveCard(card);
                    targetPlayer.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);
                }
            });

        ability = new ActivatedAbility(
            source: crypt,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(crypt),
                AdditionalCost.Sacrifice(crypt), // self-sac; zone move in effect closure
            },
            effects: new IEffect[] { exileEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        crypt.AddAbility(ability);

        return crypt;
    }
}
