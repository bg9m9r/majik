using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Field of Ruin (Ixalan).
///
/// Land.
/// Oracle text:
///   "{T}: Add {C}.
///    {1}, {T}, Sacrifice Field of Ruin: Destroy target nonbasic land an
///    opponent controls. Each player searches their library for a basic
///    land card, puts it onto the battlefield, then shuffles."
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{1}, {T}, Sacrifice Field of Ruin: Destroy target nonbasic land
///   an opponent controls.</b> — wired as an <see cref="ActivatedAbility"/>
///   with three costs:
///     - <see cref="ManaCostCost"/> {1}
///     - <see cref="AdditionalCost.Tap"/>
///     - Self-sacrifice performed inside the resolution closure (mirrors
///       <see cref="WastelandFactory"/>'s posture while
///       <see cref="AdditionalCost.Sacrifice"/>'s zone-move primitive is
///       still a stub).
///   A 1..1 <see cref="TargetRequest"/> declares "target nonbasic land
///   an opponent controls" — the resolution body gates on (a) Land type,
///   (b) NOT Basic supertype, (c) controlled by a player that is not the
///   activator (CR 608.2b — illegal target → effect does nothing).
/// - <b>Each player searches their library for a basic land card, puts
///   it onto the battlefield, then shuffles</b> — runs after the destroy.
///   For each player in turn order (active player first per CR 101.4 /
///   CR 603.3b APNAP), the resolution body finds the basic land
///   candidates in their library, asks the player's agent to pick (CR
///   701.19a — players may decline; null pick = no land found / not
///   chosen), moves the picked land via <see cref="ZoneServiceRegistry"/>
///   (so ETB triggers fire), then shuffles the player's library (CR
///   701.20a). All four ETB / shuffle events are independent — each
///   player's tutor is its own search.
///
/// ## Deferred (v1 gaps)
/// - <b>"Each player" turn-order</b>: CR 603.3b APNAP isn't enforced at
///   the agent-prompt level here — the factory walks the supplied player
///   list in registry order. For one-on-one games (the engine's primary
///   shape today) the observable behaviour matches: each player gets
///   exactly one tutor.
/// - <b>AdditionalCost.Sacrifice</b>: self-sac payment is inlined into
///   the resolution closure (Wasteland / Engineered Explosives posture)
///   until the shared primitive ships a zone-move side-effect.
/// - <b>Agent target legality filtering</b>: ActionValidator does not
///   yet narrow the agent's candidate pool to nonbasic-opponent-lands.
///   Resolution-time guards catch illegal picks.
/// - <b>Reveal event</b>: same gap as Cultivate / Rampant Growth — the
///   tutored land moves Library → Battlefield without a reveal event.
/// </summary>
[CardName("Field of Ruin")]
public static class FieldOfRuinFactory
{
    public const string CardName = "Field of Ruin";

    // Basic land names per CR 305.6.
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    public static Land Create(Player owner) => Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Field of Ruin.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="allPlayersResolver">Late-bound enumerator of all
    /// players in the game (active player first by convention — CR
    /// 101.4). May be null — the destroy half still runs against the
    /// chosen target, but the each-player tutor half no-ops (no player
    /// list to walk). Mirrors Ashiok's <c>allPlayersResolver</c>
    /// posture.</param>
    public static Land Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {C}
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("{C}")));

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice Field of Ruin: Destroy target nonbasic land
        // an opponent controls. Each player searches their library for a
        // basic land card, puts it onto the battlefield, then shuffles.
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            $"{CardName}: destroy target nonbasic land an opponent controls; each player tutors a basic to battlefield",
            () =>
            {
                if (destroyAbility == null) return;

                // Self-sacrifice — Wasteland posture (AdditionalCost.Sacrifice
                // is a stub today; the cost was declared at activation, the
                // visible zone-move catches up here ahead of the destroy /
                // tutor steps).
                SacrificeToOwnersGraveyard(land);

                // Destroy half — gate the chosen target then route it to
                // owner's graveyard (CR 608.2b — illegal target → effect
                // does nothing for that target; the each-player tutor half
                // still runs per the printed comma-joined effect text).
                var chosen = destroyAbility.ChosenTargets.Count > 0
                    ? destroyAbility.ChosenTargets[0]
                    : Array.Empty<object>();
                if (chosen.Count > 0
                    && chosen[0] is ICard target
                    && target.HasType(CardType.Land)
                    && !target.HasSupertype(CardSupertype.Basic)
                    && target.Zone == ZoneType.Battlefield
                    && target.Controller != null
                    && !ReferenceEquals(target.Controller, land.Controller ?? owner))
                {
                    DestroyToOwnersGraveyard(target);
                }

                // Each-player tutor half (CR 701.19a) — runs unconditionally
                // (the destroy and the tutor are joined with a period in
                // the oracle; an illegal destroy doesn't suppress the
                // tutor).
                var players = allPlayersResolver?.Invoke();
                if (players == null) return;

                foreach (var p in players)
                {
                    TutorBasicLandToBattlefield(p);
                }
            });

        destroyAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { destroyEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonbasic land an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        land.AddAbility(destroyAbility);

        return land;
    }

    /// <summary>
    /// Helper: ask <paramref name="player"/>'s agent to find a basic land
    /// in their library (CR 701.19a), route the pick onto the battlefield
    /// via <see cref="ZoneService"/> when one is wired (so ETB triggers
    /// fire), then shuffle the library (CR 701.20a). Declined / empty-
    /// candidate searches still trigger the shuffle.
    /// </summary>
    private static void TutorBasicLandToBattlefield(Player player)
    {
        if (player == null) return;

        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
            .ToList();

        ICard? pick = null;
        if (candidates.Count > 0)
        {
            var agent = AgentRegistry.Get(player);
            // Build a minimal pick context — the engine's BuildPickContext
            // helper isn't reachable from a factory (internal), so we hand
            // the agent the candidate list and a label and let it score
            // (the LibraryPickPolicy reads off the candidate list only for
            // basic-land prompts — basic picks are uniform).
            // ctx may be null in v1 factory closures (IPlayerAgent
            // contract). The basic-land tutor is uniform across picks so
            // LibraryPickPolicy doesn't need board context for scoring.
            pick = agent != null
                ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates,
                        "basic land card")
                    .GetAwaiter().GetResult()
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
            }
        }

        // CR 701.20a — a search effect shuffles the searched library
        // even if nothing was found.
        LibraryShuffle.ShuffleLibrary(player, "Field of Ruin tutor");
    }

    private static void SacrificeToOwnersGraveyard(Land self)
    {
        var ownerOfSelf = self.Owner;
        if (ownerOfSelf == null) return;
        if (self.Zone != ZoneType.Battlefield) return;

        var holder = self.Controller ?? ownerOfSelf;
        holder.Zones.Battlefield.RemoveCard(self);
        ownerOfSelf.Zones.Graveyard.AddCard(self);
        self.SetZone(ZoneType.Graveyard);
    }

    private static void DestroyToOwnersGraveyard(ICard card)
    {
        var ownerOfCard = card.Owner;
        if (ownerOfCard == null) return;

        var holder = card.Controller ?? ownerOfCard;
        holder.Zones.Battlefield.RemoveCard(card);
        ownerOfCard.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
