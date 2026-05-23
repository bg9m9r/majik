using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Karn Liberated (New Phyrexia, {7}).
///
/// Legendary Planeswalker — Karn, starting loyalty 6.
/// Oracle text:
///   "+4: Target player exiles a card from their hand.
///    -3: Exile target permanent.
///    -14: Restart the game, leaving in exile all non-Aura permanent cards
///         exiled with Karn Liberated. Then put those cards onto the
///         battlefield under your control."
///
/// ## Implemented (v1)
/// - Legendary Planeswalker with loyalty 6, Karn subtype, mana cost {7}.
/// - <b>+4</b>: target-player-exiles-a-card-from-hand. v1 auto-pick: the
///   first opponent in the supplied <paramref name="allPlayersResolver"/>
///   exiles the first card in their hand (CR 701.21). With no resolver
///   wired the effect no-ops while the loyalty change still applies
///   (CR 606.3).
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
///   preserved non-Aura cards. Wiring it requires multiple sessions of
///   coordinated work across <see cref="Majik.Core.Domain.Aggregates"/>,
///   the state machines, and the zone service. The loyalty ability is
///   present at -14 with an empty effect so the cost (loyalty change) is
///   still paid (CR 606.3) and dispatcher-shape tests pass.
/// - <b>Targeting prompts</b>: <see cref="LoyaltyAbility"/> doesn't yet
///   declare <see cref="TargetRequest"/>s. +4 picks the first opponent
///   and -3 picks the first matching permanent deterministically rather
///   than via the agent. Hand-card choice for +4 is similarly
///   deterministic (first card in the target player's hand).
/// </summary>
public static class KarnLiberatedFactory
{
    public const string CardName = "Karn Liberated";
    public const string Cost = "{7}";
    public const int StartingLoyalty = 6;

    /// <summary>
    /// Construct Karn Liberated with no resolvers wired. Suitable for
    /// shape / dispatcher tests — +4 and -3 will no-op; loyalty changes
    /// still apply.
    /// </summary>
    public static Planeswalker Create(Player owner) =>
        Create(owner, allPlayersResolver: null, targetResolver: null);

    /// <summary>
    /// Construct Karn Liberated. When
    /// <paramref name="allPlayersResolver"/> is non-null the +4 hand-exile
    /// targets the first opponent. When <paramref name="targetResolver"/>
    /// is non-null the -3 exiles the first permanent it returns.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Returns the full player list at
    /// activation time. v1 picks the first non-owner as the +4 target.
    /// May be null — +4 no-ops.</param>
    /// <param name="targetResolver">Returns candidate target permanents
    /// for -3 (any permanent, any controller). v1 picks the first
    /// permanent returned. May be null — -3 no-ops.</param>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
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
        // v1 auto-pick: first opponent in the player list exiles the first
        // card in their hand. CR 701.21 (exile a card from a zone). With
        // no resolver wired the effect is silent.
        karn.AddAbility(new LoyaltyAbility(karn, +4, () =>
        {
            var players = allPlayersResolver?.Invoke();
            if (players == null) return;
            foreach (var p in players)
            {
                if (ReferenceEquals(p, owner)) continue;
                var pick = p.Zones.Hand.GetCards().FirstOrDefault();
                if (pick == null) continue;
                p.Zones.Hand.RemoveCard(pick);
                p.Zones.Exile.AddCard(pick);
                pick.SetZone(ZoneType.Exile);
                return; // CR 700.6 — "target player" is one player
            }
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
        //    permanent cards exiled with Karn Liberated, then put them onto
        //    the battlefield under your control. v1 DEFERRED — shipped as
        //    a no-op so the loyalty change (and "this card is a legal
        //    -14 ability") still apply (CR 606.3). Restart-the-game is
        //    engine-foundational and out of scope for the card-ship slice.
        karn.AddAbility(new LoyaltyAbility(karn, -14, () => { /* deferred — restart-the-game */ }));

        return karn;
    }
}
