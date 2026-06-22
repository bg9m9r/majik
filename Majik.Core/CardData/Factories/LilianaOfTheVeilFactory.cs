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
/// - <b>+1</b>: each-player-discards-a-card. <b>Pure-enumeration each-player
///   effect</b>: reads the player list off the LIVE
///   <see cref="ResolutionContext"/> (<c>rc.Game.AllPlayers</c>) at
///   resolution — no captured player-list resolver, so it runs on the prod
///   routed build (the <c>resolver-null-loyalty-each-player-context-read</c>
///   deferral fix; same context-read pattern as #2549 / #2551, on the loyalty
///   path). <b>Each player chooses THEIR OWN card</b> (CR 701.16a): the
///   per-player agent is resolved in priority order — explicit agentSelector,
///   then <c>rc.Agent</c> for the activating controller, then the per-game
///   <see cref="AgentRegistry"/> seam for the other seat(s) (the #2543 /
///   #2551b each-player-agent-choice pattern). The chosen card is discarded
///   via <see cref="Fx.DiscardCard"/>, so <c>DiscardedEvent</c> / madness
///   fire. A seat with no agent (headless / shape-only paths) falls back to
///   first-card-in-hand (CR-legal deterministic pick). With no live game
///   context the effect silently no-ops while the loyalty change still applies
///   (CR 606.5 semantics).
/// - <b>-2</b>: target-player-sacs-a-creature. Reads the chosen player off
///   the <see cref="ResolutionContext"/> (slot 0); on the legacy direct-
///   activation path it falls back to the first opponent with a creature read
///   off <see cref="ContextOpponents"/> (live game context — no captured
///   resolver). That player sacrifices the first creature on their
///   battlefield.
///
/// ## Deferred (v1 gaps)
/// - <b>Targeting prompts</b>: LoyaltyAbility does not yet declare
///   <see cref="TargetRequest"/>s for the discard count; -2 declares a
///   target-player request but picks the creature deterministically rather
///   than via the sacrificing player's agent. Wiring full loyalty-target
///   plumbing is out of scope here.
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
    /// Construct Liliana of the Veil. The +1 / -2 effects read the live game
    /// off the <see cref="ResolutionContext"/> at resolution, so they run on
    /// the production routed build (<c>NamedCardFactory.Create(name, owner)</c>)
    /// with no captured resolver. Each player chooses their own discard via
    /// their per-game <see cref="AgentRegistry"/> agent (first-in-hand only
    /// when a seat has no agent).
    /// </summary>
    public static Planeswalker Create(Player owner)
        => Create(owner, agentSelector: null);

    /// <summary>
    /// Construct Liliana of the Veil with optional per-player
    /// <see cref="IPlayerAgent"/> selector. The +1 ability consults
    /// <see cref="IPlayerAgent.ChooseFromHandAsync"/>
    /// (<see cref="BotIntent.Discard"/>) per player for the discard pick: the
    /// explicit <paramref name="agentSelector"/> wins, then <c>rc.Agent</c> for
    /// the activating controller, then the per-game <see cref="AgentRegistry"/>
    /// seam for the other seat(s) — so each player chooses their own card
    /// (CR 701.16a). A seat with no agent falls back to first-card-in-hand. The
    /// player list is always read off the live resolution context.
    /// </summary>
    public static Planeswalker Create(
        Player owner,
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
        // CR 701.16a — each player chooses their own card to discard. The
        // player list is read off the LIVE ResolutionContext (rc.Game.AllPlayers)
        // at resolution — NOT from a build-time captured resolver (the prod
        // routed single-arg Create left it null → the clause used to be INERT
        // in real games; the resolver-null loyalty deferral fix). Each player's
        // OWN agent is consulted via ChooseFromHandAsync(BotIntent.Discard) —
        // resolved from agentSelector / rc.Agent (controller) / AgentRegistry
        // (other seats); the discard routes through Fx.DiscardCard so
        // DiscardedEvent / madness fire. No-agent seat: first card in hand.
        liliana.AddAbility(new LoyaltyAbility(liliana, +1,
            new[]
            {
                Fx.Inline("Each player discards a card", rc =>
                {
                    var players = rc.Game?.AllPlayers;
                    if (players == null) return default;
                    foreach (var p in players)
                    {
                        if (p == null) continue;
                        var hand = p.Zones.Hand.GetCards().ToList();
                        if (hand.Count == 0) continue;

                        // CR 701.16a / CR 118.x — EACH player chooses THEIR OWN
                        // card. Resolve the per-player agent in priority order:
                        //   1. explicit agentSelector (test/back-compat seam),
                        //   2. rc.Agent when p is the activating controller
                        //      (the resolver-supplied agent already on context),
                        //   3. the per-game AgentRegistry.Get(p) seam (the
                        //      #2543 / #2551b each-player-agent-choice pattern —
                        //      this is what the live routed build uses for the
                        //      non-controller seat(s)).
                        // No agent for a seat → first-card-in-hand (CR-legal
                        // deterministic fallback for headless/shape-only paths).
                        var agent = agentSelector?.Invoke(p)
                            ?? (ReferenceEquals(p, rc.Controller) ? rc.Agent : null)
                            ?? AgentRegistry.Get(p);
                        ICard pick;
                        if (agent != null)
                        {
                            var chosen = agent
                                .ChooseFromHandAsync(p, hand, BotIntent.Discard)
                                .GetAwaiter().GetResult();
                            pick = (chosen != null && chosen.Zone == ZoneType.Hand)
                                ? chosen
                                : hand[0];
                        }
                        else
                        {
                            pick = hand[0];
                        }

                        // CR 701.8 — route through the central discard chokepoint
                        // so DiscardedEvent fires (madness / "whenever you
                        // discard …" triggers observe it). wasCost: false — this
                        // is an effect discard, not a cost.
                        Fx.DiscardCard(p, pick, wasCost: false);
                    }
                    return default;
                }),
            }));

        // -- -2: Target player sacrifices a creature. ----------------------
        // CR 606 (loyalty) + CR 115 (target player) + CR 701.17 (sacrifice).
        // The target player is chosen by the activating player's agent via a
        // TargetRequest; the effect reads the chosen player off the
        // ResolutionContext (slot 0), falling back to "first opponent with a
        // creature" read off ContextOpponents (the live game context — no
        // captured resolver) on the legacy direct-activation path. That player
        // then sacrifices the first creature on their battlefield (v1
        // deterministic pick).
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
                        ?? ContextOpponents.Of(rc, rc.Controller)
                            .FirstOrDefault(p => p.Zones.Battlefield.GetCards().OfType<Creature>().Any());
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
