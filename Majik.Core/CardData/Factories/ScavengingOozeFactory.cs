using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scavenging Ooze (Commander 2011, {1}{G}).
///
/// Creature — Ooze 2/2. Oracle text:
///   "{G}: Exile target creature card from a graveyard. If you do, put a
///    +1/+1 counter on Scavenging Ooze. You gain 1 life."
///
/// ## Implemented (v1)
/// - 2/2 Ooze with mana cost {1}{G}, owner/controller assigned.
/// - <b>Activated ability ({G})</b>: at resolution, scans every graveyard
///   reachable via <see cref="Create(Player, Func{IReadOnlyList{Player}}?)"/>'s
///   <c>allPlayersResolver</c> (falling back to the controller's own
///   graveyard when no resolver is supplied) and exiles the first creature
///   card found. On a successful exile, puts a +1/+1 counter on the Ooze
///   and the controller gains 1 life (CR 119.3). When no creature card
///   exists in any reachable graveyard the activation resolves as a no-op
///   (counter + life rider are gated on a successful exile by the "If you
///   do" clause — CR 605.x conditional payoff semantics).
///
/// ## Deferred (v1 gaps)
/// - <b>Graveyard target prompt</b>: "target creature card from a
///   graveyard" should prompt the controller's agent for any creature card
///   across all graveyards (CR 109.1 / 115.1). v1 deterministically picks
///   the first creature card found, scanning the controller's graveyard
///   first then each other player's graveyard in resolver order.
/// - <b>Target legality re-check at resolution</b>: CR 608.2b — if the
///   targeted card has left the graveyard before resolution, the entire
///   ability does nothing (counter + life riders are skipped because the
///   exile did not happen). v1 picks at resolution, so it doesn't need
///   the re-check; once true targeting lands here, the re-check must be
///   wired so the "If you do" rider correctly no-ops.
/// </summary>
[CardName("Scavenging Ooze")]
public static class ScavengingOozeFactory
{
    /// <summary>
    /// Construct Scavenging Ooze. The activated ability scans every player's
    /// graveyard for the first creature card to exile (CR 109.1 — graveyard
    /// cards are public across all players), reading the players from the LIVE
    /// resolution context (<c>ctx.Game.AllPlayers</c>) at resolution — no
    /// captured player resolver, so it is correct on the production routed
    /// build (mirrors #2551). With no live game context only the controller's
    /// graveyard is reachable (shape-only paths). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Scavenging Ooze",
            manaCost: "{1}{G}",
            power: 2,
            toughness: 2,
            subtypes: new[] { CardSubtype.Ooze });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // {G}: Exile target creature card from a graveyard.
        // If you do, put a +1/+1 counter on Scavenging Ooze. You gain 1
        // life.
        //
        // CR 605 — not a mana ability (has a non-mana effect); goes on the
        // stack. CR 117.1a — choices at resolution.
        // v1: deterministic creature-card picker — see class xmldoc.
        // ----------------------------------------------------------------
        var exileEffect = new Effect(
            "Scavenging Ooze: exile creature from graveyard, then +1/+1 + 1 life",
            ctx =>
            {
                // Build the list of graveyards to scan. Controller first, then
                // any additional players read off the LIVE game at resolution
                // (ctx.Game.AllPlayers, with owner deduplicated). No captured
                // resolver, so correct on the routed prod build.
                var graveyardOwners = new List<Player> { owner };
                var extra = ctx.Game?.AllPlayers;
                if (extra != null)
                {
                    foreach (var p in extra)
                    {
                        if (!ReferenceEquals(p, owner)) graveyardOwners.Add(p);
                    }
                }

                Player? targetOwner = null;
                ICard? target = null;
                foreach (var p in graveyardOwners)
                {
                    var pick = p.Zones.Graveyard.GetCards()
                        .FirstOrDefault(c => c.HasType(CardType.Creature));
                    if (pick != null)
                    {
                        targetOwner = p;
                        target = pick;
                        break;
                    }
                }

                // "If you do" — no creature card found, no exile, so the
                // counter + life riders are skipped (CR 605.x conditional
                // payoff).
                if (target == null || targetOwner == null) return ValueTask.CompletedTask;

                targetOwner.Zones.Graveyard.RemoveCard(target);
                targetOwner.Zones.Exile.AddCard(target);
                target.SetZone(ZoneType.Exile);

                // +1/+1 counter on Scavenging Ooze itself.
                card.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);

                // Controller gains 1 life (CR 119.3).
                owner.GainLife(1);

                return ValueTask.CompletedTask;
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{G}") },
            effects: new IEffect[] { exileEffect });

        card.AddAbility(activated);

        return card;
    }
}
