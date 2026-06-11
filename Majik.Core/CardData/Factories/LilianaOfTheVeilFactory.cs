using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Liliana of the Veil (Innistrad, {1}{B}{B}).
///
/// Legendary Planeswalker — Liliana, starting loyalty 3.
/// Oracle text:
///   "+1: Each player discards a card.
///    −2: Target player sacrifices a creature.
///    −6: Separate all permanents target player controls into two piles.
///         That player sacrifices all permanents in the pile of their choice."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 3, Liliana subtype, mana cost
///   {1}{B}{B}.
/// - <b>+1</b>: each-player-discards-a-card. Iterates the player list from
///   the optional <paramref name="allPlayersResolver"/> (see
///   <see cref="Create(Player, Func{IReadOnlyList{Player}}?)"/>). For each
///   player with at least one card in hand, the first card in hand is moved
///   to graveyard (v1 deterministic pick, mirroring
///   <see cref="YawgmothFactory"/>). With no resolver wired, the effect
///   silently no-ops while the loyalty change still applies (CR 606.5
///   semantics).
/// - <b>-2</b>: target-player-sacs-a-creature. v1: auto-picks an opponent
///   (the first non-controller player in
///   <paramref name="allPlayersResolver"/>) and forces them to sacrifice
///   the first creature on their battlefield. With no resolver wired the
///   effect is silent.
///
/// ## Deferred (v1 gaps)
/// - <b>Targeting prompts</b>: LoyaltyAbility does not yet declare
///   <see cref="TargetRequest"/>s. -2 picks the opponent + creature
///   deterministically rather than via the agent. Wiring loyalty-target
///   plumbing is out of scope here.
/// - <b>Discard choice</b>: the printed card asks "each player discards a
///   card" with each player choosing their own card. v1 picks the first
///   card in hand (matches Yawgmoth's v1 simplification).
/// - <b>-6 ultimate</b>: pile-split is a multi-stage interactive effect
///   (one player partitions, the other chooses which pile to sacrifice).
///   No "split into piles" primitive exists in the engine yet. The
///   loyalty ability is wired with a no-op body so the loyalty change
///   still applies (CR 606.3 — the cost is paid even if the effect
///   does nothing).
/// </summary>
[CardName("Liliana of the Veil")]
public static class LilianaOfTheVeilFactory
{
    /// <summary>
    /// Construct Liliana of the Veil with no player-list resolver. The +1
    /// and -2 effects no-op; loyalty changes still apply. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Planeswalker Create(Player owner)
        => Create(owner, allPlayersResolver: null, agentSelector: null);

    /// <summary>
    /// Construct Liliana of the Veil. When
    /// <paramref name="allPlayersResolver"/> is non-null, the +1 and -2
    /// effects iterate the full player list at activation time and apply
    /// their v1 deterministic effects (see class xmldoc).
    /// </summary>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
        => Create(owner, allPlayersResolver, agentSelector: null);

    /// <summary>
    /// Construct Liliana of the Veil with optional per-player
    /// <see cref="IPlayerAgent"/> selector. When supplied, the +1 ability
    /// consults <see cref="IPlayerAgent.ChooseFromHandAsync"/>
    /// (<see cref="BotIntent.Discard"/>) per player for the discard pick.
    /// Null preserves the legacy first-card-in-hand pick (CR 701.16a).
    /// </summary>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var liliana = new Planeswalker(
            name: "Liliana of the Veil",
            manaCost: "{1}{B}{B}",
            startingLoyalty: 3,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Liliana });

        liliana.SetOwner(owner);
        liliana.SetController(owner);

        // -- +1: Each player discards a card. -------------------------------
        // CR 701.16a — each player chooses their own card to discard.
        // Agent path (per-player IPlayerAgent via selector): consult
        // ChooseFromHandAsync(BotIntent.Discard); the heuristic bot's
        // override pitches the highest-MV card. No-agent path: first card
        // in hand (legacy deterministic v1 — matches Yawgmoth shape).
        liliana.AddAbility(new LoyaltyAbility(liliana, +1, () =>
        {
            var players = allPlayersResolver?.Invoke();
            if (players == null) return;
            foreach (var p in players)
            {
                var hand = p.Zones.Hand.GetCards().ToList();
                if (hand.Count == 0) continue;
                var agent = agentSelector?.Invoke(p);
                ICard? pick;
                if (agent != null)
                {
                    pick = agent.ChooseFromHandAsync(p, hand, BotIntent.Discard)
                        .GetAwaiter().GetResult();
                    if (pick == null || pick.Zone != ZoneType.Hand)
                        pick = hand[0];
                }
                else
                {
                    pick = hand[0];
                }
                p.Zones.Hand.RemoveCard(pick);
                p.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            }
        }));

        // -- -2: Target player sacrifices a creature. ----------------------
        // CR 606 (loyalty) + CR 115 (target player) + CR 701.17 (sacrifice).
        // The target player is chosen by the activating player's agent via a
        // TargetRequest; the effect reads the chosen player off the
        // ResolutionContext (slot 0), falling back to "first opponent with a
        // creature" on the legacy direct-activation path. That player then
        // sacrifices the first creature on their battlefield (v1 deterministic
        // pick — the sacrificing player chooses which creature; modelled as
        // first-creature here, matching the +1 discard simplification).
        var sacrificePlayerRequest = new TargetRequest(
            Description: "Target player sacrifices a creature",
            MinTargets: 1,
            MaxTargets: 1,
            LegalCandidates: Array.Empty<object>(),
            Intent: BotIntent.Removal,
            CandidateGatherer: gameCtx => gameCtx.AllPlayers
                .Cast<object>()
                .ToList());

        liliana.AddAbility(new LoyaltyAbility(
            liliana,
            -2,
            new[]
            {
                Fx.Inline("Target player sacrifices a creature", rc =>
                {
                    var target = (rc.ChosenTargets.Count > 0 && rc.ChosenTargets[0].Count > 0
                        ? rc.ChosenTargets[0][0] as Player
                        : null)
                        ?? allPlayersResolver?.Invoke()?
                            .FirstOrDefault(p => !ReferenceEquals(p, owner)
                                && p.Zones.Battlefield.GetCards().OfType<Creature>().Any());
                    if (target == null) return default;

                    var victim = target.Zones.Battlefield.GetCards()
                        .OfType<Creature>().FirstOrDefault();
                    if (victim == null) return default;
                    target.Zones.Battlefield.RemoveCard(victim);
                    target.Zones.Graveyard.AddCard(victim);
                    victim.SetZone(ZoneType.Graveyard);
                    return default;
                }),
            },
            targetRequests: new[] { sacrificePlayerRequest }));

        // -- -6 ultimate: pile split. v1 deferred — loyalty change applies
        //    with an empty body so the cost is still paid.
        liliana.AddAbility(new LoyaltyAbility(liliana, -6, () => { /* deferred */ }));

        return liliana;
    }
}
