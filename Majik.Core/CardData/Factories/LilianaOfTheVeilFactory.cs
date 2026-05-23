using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
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
public static class LilianaOfTheVeilFactory
{
    /// <summary>
    /// Construct Liliana of the Veil with no player-list resolver. The +1
    /// and -2 effects no-op; loyalty changes still apply. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Planeswalker Create(Player owner) => Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Liliana of the Veil. When
    /// <paramref name="allPlayersResolver"/> is non-null, the +1 and -2
    /// effects iterate the full player list at activation time and apply
    /// their v1 deterministic effects (see class xmldoc).
    /// </summary>
    public static Planeswalker Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
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
        liliana.AddAbility(new LoyaltyAbility(liliana, +1, () =>
        {
            var players = allPlayersResolver?.Invoke();
            if (players == null) return;
            foreach (var p in players)
            {
                var pick = p.Zones.Hand.GetCards().FirstOrDefault();
                if (pick == null) continue;
                p.Zones.Hand.RemoveCard(pick);
                p.Zones.Graveyard.AddCard(pick);
                pick.SetZone(ZoneType.Graveyard);
            }
        }));

        // -- -2: Target player sacrifices a creature. ----------------------
        // v1 auto-pick: target the first opponent in the player list with a
        // creature; sacrifice the first creature on their battlefield.
        liliana.AddAbility(new LoyaltyAbility(liliana, -2, () =>
        {
            var players = allPlayersResolver?.Invoke();
            if (players == null) return;
            foreach (var p in players)
            {
                if (ReferenceEquals(p, owner)) continue;
                var victim = p.Zones.Battlefield.GetCards()
                    .OfType<Creature>().FirstOrDefault();
                if (victim == null) continue;
                p.Zones.Battlefield.RemoveCard(victim);
                p.Zones.Graveyard.AddCard(victim);
                victim.SetZone(ZoneType.Graveyard);
                return; // CR 700.6 — "target player" is one player
            }
        }));

        // -- -6 ultimate: pile split. v1 deferred — loyalty change applies
        //    with an empty body so the cost is still paid.
        liliana.AddAbility(new LoyaltyAbility(liliana, -6, () => { /* deferred */ }));

        return liliana;
    }
}
