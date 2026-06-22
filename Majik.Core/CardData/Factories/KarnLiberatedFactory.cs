using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Karn Liberated (New Phyrexia, {7}).
///
/// Legendary Planeswalker — Karn, starting loyalty 6.
/// Oracle text (verified against Scryfall):
///   "+4: Target player exiles a card from their hand.
///    −3: Exile target permanent.
///    −14: Restart the game, leaving in exile all non-Aura permanent cards
///         exiled with Karn. Then put those cards onto the battlefield under
///         your control."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 6, Karn subtype, mana cost {7}.
/// - <b>+4</b>: target-player-exiles-a-card-from-hand. The activating player
///   chooses the target player via
///   <see cref="Players.Agents.IPlayerAgent.ChoosePlayerAsync"/> over the live
///   game's players (CR 109.1 / 601.2c), read off the resolution context
///   (<c>rc.Agent</c> + <c>rc.Game</c>) instead of a factory-captured resolver.
///   The chosen player then exiles the first card in their hand (CR 701.21).
///   On the shape-only path (no live game) the +4 no-ops while the loyalty
///   change still applies (CR 606.3).
/// - <b>-3</b>: exile-target-permanent. v1 auto-pick: the first matching
///   permanent in the supplied <paramref name="targetResolver"/> is moved
///   to its owner's exile zone (CR 701.21). With no resolver wired the
///   effect no-ops while the loyalty change still applies.
///
/// ## Deferred (v1 gaps)
/// - <b>-14 ultimate (restart-the-game)</b>: shipped as a no-op body.
///   Restart-the-game (CR 720) is an engine-foundational mechanic —
///   teardown + rebuild of the game-state aggregate, special "exiled with
///   Karn" tracking, ETB-under-Karn's-controller re-entry of the
///   preserved non-Aura cards. The loyalty ability is present at -14 with an
///   empty effect so the cost (loyalty change) is still paid (CR 606.3) and
///   dispatcher-shape tests pass.
/// - <b>-3 targeting prompt</b>: <see cref="LoyaltyAbility"/> doesn't yet
///   declare a <see cref="TargetRequest"/> for the exile-target-permanent
///   clause; it still picks the first matching permanent from the supplied
///   <paramref name="targetResolver"/> rather than via an agent target prompt.
/// - <b>+4 hand-card choice</b>: the chosen player exiles the FIRST card in
///   their hand rather than choosing which card (the printed "exiles a card"
///   gives the choice to that player). The PLAYER choice (which player) is now
///   agent-driven; the WHICH-card sub-choice is still deterministic.
/// </summary>
[CardName("Karn Liberated")]
public static class KarnLiberatedFactory
{
    public const string CardName = "Karn Liberated";
    public const string Cost = "{7}";
    public const int StartingLoyalty = 6;

    /// <summary>
    /// Construct Karn Liberated (production routed path). The +4 reads the
    /// target player off the live resolution context; the -3 has no resolver
    /// wired and no-ops; loyalty changes still apply.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, targetResolver: null);

    /// <summary>
    /// Construct Karn Liberated. When <paramref name="targetResolver"/> is
    /// non-null the -3 exiles the first permanent it returns.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="targetResolver">Returns candidate target permanents
    /// for -3 (any permanent, any controller). v1 picks the first
    /// permanent returned. May be null — -3 no-ops.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Permanent>>? targetResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var karn = new Planeswalker(
            name: CardName,
            manaCost: Cost,
            startingLoyalty: StartingLoyalty,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Karn });

        karn.SetOwner(owner);
        karn.SetController(owner);

        // -- +4: Target player exiles a card from their hand. -------------
        // CR 109.1 / 601.2c — the activating player CHOOSES the target player.
        // The pick routes through the agent's ChoosePlayerAsync over the live
        // game's players (read off the resolution context), then that player
        // exiles the first card in their hand (CR 701.21). On the shape-only
        // path (no live game) the body is a silent no-op.
        karn.AddAbility(new LoyaltyAbility(karn, +4, new[]
        {
            new Effect(
                $"{CardName}: +4 — target player exiles a card from their hand.",
                async rc =>
                {
                    var target = await ChooseTargetPlayerAsync(owner, rc).ConfigureAwait(false);
                    if (target is null) return;

                    var pick = target.Zones.Hand.GetCards().FirstOrDefault();
                    if (pick is null) return;
                    target.Zones.Hand.RemoveCard(pick);
                    target.Zones.Exile.AddCard(pick);
                    pick.SetZone(ZoneType.Exile);
                }),
        }));

        // -- -3: Exile target permanent. ----------------------------------
        // v1 auto-pick: first permanent returned by the resolver. CR
        // 701.21 (exile to its owner's exile zone). With no resolver
        // wired the effect is silent.
        karn.AddAbility(new LoyaltyAbility(karn, -3, () =>
        {
            var candidates = targetResolver?.Invoke();
            if (candidates == null) return;
            foreach (var p in candidates)
            {
                if (p == null) continue;
                if (p.Zone != ZoneType.Battlefield) continue; // illegal at resolution
                var pOwner = p.Owner ?? owner;
                if (p.Controller != null)
                {
                    p.Controller.Zones.Battlefield.RemoveCard(p);
                }
                else
                {
                    pOwner.Zones.Battlefield.RemoveCard(p);
                }
                pOwner.Zones.Exile.AddCard(p);
                p.SetZone(ZoneType.Exile);
                return; // "target permanent" — one permanent
            }
        }));

        // -- -14 ultimate: Restart the game (CR 720), preserving non-Aura
        //    permanent cards exiled with Karn, then put them onto the
        //    battlefield under your control. v1 DEFERRED — shipped as a
        //    no-op so the loyalty change (and "this card is a legal -14
        //    ability") still apply (CR 606.3). Restart-the-game is
        //    engine-foundational and out of scope for the card-ship slice.
        karn.AddAbility(new LoyaltyAbility(karn, -14, () => { /* deferred — restart-the-game */ }));

        return karn;
    }

    /// <summary>
    /// CR 109.1 / 601.2c — choose the "target player" for the +4. The
    /// activating player's agent picks one player from the live game's player
    /// list (every in-game player is a legal "target player"; Karn's +4 is the
    /// rare loyalty ability that can name its OWN controller, CR 109.1).
    /// Routed through <see cref="Players.Agents.IPlayerAgent.ChoosePlayerAsync"/>
    /// over the live resolution context — forced in a one-legal-player game, a
    /// real choice in a multiplayer match. Returns <see langword="null"/> only
    /// when no live game context is available (shape-only path) — then the +4
    /// no-ops while the loyalty change still applies (CR 606.3).
    /// </summary>
    private static async Task<Player?> ChooseTargetPlayerAsync(Player controller, ResolutionContext rc)
    {
        var game = rc.Game;
        if (game?.AllPlayers is not { Count: > 0 } all) return null;

        // CR 800.4a — a player who has left the game is no longer a legal
        // target player. "Target player" may be ANY player, including the
        // controller (CR 109.1) — do NOT filter the controller out.
        var candidates = new List<Player>(all.Count);
        foreach (var p in all)
        {
            if (p is null || p.HasLost) continue;
            candidates.Add(p);
        }
        if (candidates.Count == 0) return null;

        var agent = rc.Agent ?? AgentRegistry.Get(controller);
        if (agent is null) return candidates[0];

        return await agent.ChoosePlayerAsync(
            game, candidates, $"{CardName}: +4 — choose target player to exile a card from hand",
            Cards.BotIntent.None, rc.Ct).ConfigureAwait(false);
    }
}
