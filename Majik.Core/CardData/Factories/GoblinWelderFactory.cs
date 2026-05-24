using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Welder (Urza's Legacy, {R}).
///
/// Creature — Goblin Artificer 1/1. Oracle text:
///   "{T}: Choose target artifact a player controls and target artifact
///    card in that player's graveyard. If both targets are still legal
///    as this ability resolves, that player sacrifices the artifact they
///    control and returns the artifact card from their graveyard to the
///    battlefield."
///
/// ## Implemented (v1)
/// - 1/1 Goblin Artificer with mana cost {R}.
/// - <b>Activated ability (CR 113.3b)</b>: <c>{T}</c> activation cost
///   (no mana). Tap cost is expressed via <see cref="AdditionalCost.Tap"/>
///   on the ability for shape inspection.
/// - <b>Resolution (sac-then-reanimate, CR 608)</b>: deterministically
///   pairs an artifact on the battlefield with an artifact card in that
///   same player's graveyard. On resolve: the artifact's controller
///   sacrifices it (moved to their graveyard), and the artifact card
///   from their graveyard is returned to the battlefield under their
///   control. Movement is routed through
///   <see cref="ZoneService.MoveCard"/> when a service is supplied so
///   ETB / dies / graveyard triggers fire (CR 603.6a). When no service
///   is supplied the factory falls back to raw zone manipulation —
///   suitable for unit / shape tests.
/// - <b>Same-player constraint</b>: both halves must reference the same
///   player. The resolution scans candidate players in order and picks
///   the first one with a legal (battlefield artifact, graveyard
///   artifact card) pair.
///
/// ## Deferred (v1 gaps)
/// - <b>Target selection prompt</b>: targets are auto-picked (first
///   valid pair). The agent-prompt MVP will eventually let the
///   activator choose targets explicitly. Targeting legality is
///   enforced at the shape level — both halves must reference the
///   same player.
/// - <b>Legality check on resolve</b>: CR 608.2b — if a target becomes
///   illegal between activation and resolution the ability does
///   nothing. v1 re-resolves targets at execution time (effect body
///   is the resolution path), so any zone change between cost payment
///   and resolution naturally lapses the pair. A formal stack /
///   target-snapshot path is deferred to the targeting MVP.
/// - <b>Game-wide player iteration</b>: the v1 engine does not yet
///   expose a global player iterator from inside an effect body. The
///   factory accepts an optional <c>playerProvider</c> closure that
///   returns the candidate players to scan — runtime callers pass
///   <c>() => game.Players</c>. The single-arg dispatcher path leaves
///   it null and the effect body no-ops (shape only).
/// </summary>
[CardName("Goblin Welder")]
public static class GoblinWelderFactory
{
    /// <summary>
    /// Construct Goblin Welder with no live ZoneService / player-iterator
    /// wiring (the shape / dispatcher path). The activated ability is
    /// attached but its effect body no-ops because there is no candidate-
    /// player iterator. Tests that exercise the resolution behaviour
    /// either supply a <c>playerProvider</c> via the runtime overload or
    /// call <see cref="WeldResolve"/> directly.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, playerProvider: null);

    /// <summary>
    /// Construct Goblin Welder with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied, both the sacrifice
    /// (battlefield → graveyard) and the reanimation (graveyard →
    /// battlefield) are routed through <see cref="ZoneService.MoveCard"/>
    /// so dies / ETB triggers fire on the affected cards (CR 603.6a).
    /// <paramref name="playerProvider"/> returns the candidate players
    /// to scan for a legal (battlefield artifact, graveyard artifact)
    /// pair at resolution time.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        Func<IEnumerable<Player>>? playerProvider)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Goblin Welder",
            manaCost: "{R}",
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Artificer });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Activated ability — {T}: sac-then-reanimate a same-player
        // artifact pair (battlefield ↔ graveyard). CR 113.3b / 608.
        //
        // Tap cost is expressed via AdditionalCost.Tap(card) for shape
        // inspection (mirrors Stoneforge Mystic / Mox Opal). The
        // resolution body picks a (battlefield artifact, graveyard
        // artifact) pair belonging to the same player and performs the
        // sac + reanimate via ZoneService when available, otherwise via
        // raw zone manipulation.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            "Goblin Welder: sacrifice target artifact, reanimate artifact card from same player's graveyard",
            () =>
            {
                var players = playerProvider?.Invoke();
                if (players == null) return;
                WeldResolve(players, zoneService);
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// Resolve a Goblin Welder activation given an explicit player
    /// universe (typically the two players in a 2-player game). The
    /// first player with a legal (battlefield artifact, graveyard
    /// artifact card) pair wins and the weld is performed against that
    /// pair.
    /// </summary>
    /// <param name="players">Players to scan for legal pairs. Order is
    /// deterministic — the first player with a valid pair wins.</param>
    /// <param name="zoneService">Optional ZoneService for trigger-aware
    /// zone moves. When null the helper falls back to raw zone
    /// manipulation.</param>
    /// <returns><c>true</c> when a pair was found and the weld
    /// completed; <c>false</c> when no legal pair existed (no-op,
    /// CR 117.x).</returns>
    public static bool WeldResolve(
        IEnumerable<Player> players,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(players);

        foreach (var player in players)
        {
            var battlefieldArtifact = player.Zones.Battlefield.GetCards()
                .FirstOrDefault(c =>
                    (c is Artifact || c.HasType(CardType.Artifact))
                    && ReferenceEquals(c.Controller, player));

            if (battlefieldArtifact == null) continue;

            // Look for an artifact card in the same player's graveyard.
            // Artifact cards in the graveyard may be hydrated as either
            // an Artifact instance or a card whose primary type is
            // Artifact (CardType.Artifact). Prefer the strongly-typed
            // Artifact shape, then fall back to HasType.
            var graveyardArtifact = player.Zones.Graveyard.GetCards()
                .FirstOrDefault(c => c is Artifact || c.HasType(CardType.Artifact));

            if (graveyardArtifact == null) continue;

            PerformWeld(player, battlefieldArtifact, graveyardArtifact, zoneService);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Perform the sac-then-reanimate for an already-chosen
    /// (battlefield artifact, graveyard artifact) pair belonging to
    /// <paramref name="player"/>.
    /// </summary>
    private static void PerformWeld(
        Player player,
        ICard battlefieldArtifact,
        ICard graveyardArtifact,
        ZoneService? zoneService)
    {
        // Sacrifice the battlefield artifact (CR 701.16). Sacrifice
        // moves the permanent to its owner's graveyard.
        var sacOwner = battlefieldArtifact.Owner ?? player;
        if (zoneService != null)
        {
            zoneService.MoveCard(
                battlefieldArtifact,
                ZoneType.Battlefield,
                ZoneType.Graveyard,
                sacOwner);
        }
        else
        {
            player.Zones.Battlefield.RemoveCard(battlefieldArtifact);
            sacOwner.Zones.Graveyard.AddCard(battlefieldArtifact);
            battlefieldArtifact.SetZone(ZoneType.Graveyard);
        }

        // Reanimate the graveyard artifact to the same player's
        // battlefield (CR 603.6a — route through ZoneService when
        // available so ETB triggers fire). The graveyard owner takes
        // control of the reanimated artifact (CR 110.2).
        if (zoneService != null)
        {
            zoneService.MoveCard(
                graveyardArtifact,
                ZoneType.Graveyard,
                ZoneType.Battlefield,
                player);
        }
        else
        {
            player.Zones.Graveyard.RemoveCard(graveyardArtifact);
            player.Zones.Battlefield.AddCard(graveyardArtifact);
            graveyardArtifact.SetZone(ZoneType.Battlefield);
            graveyardArtifact.SetController(player);
        }
    }
}
