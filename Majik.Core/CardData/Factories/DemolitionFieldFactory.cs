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
/// Named-card factory for Demolition Field (March of the Machine
/// Commander / Foundations).
///
/// Land.
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {2}, {T}, Sacrifice this land: Destroy target nonbasic land an
///    opponent controls. That land's controller may search their library
///    for a basic land card, put it onto the battlefield, then shuffle.
///    You may search your library for a basic land card, put it onto the
///    battlefield, then shuffle."
///
/// Demolition Field is a near-twin of <see cref="FieldOfRuinFactory"/>;
/// it differs in two ways and is implemented with the same primitives:
///   1. The activation cost is <b>{2}</b> (Field of Ruin is {1}).
///   2. The tutor rider is NOT "each player" — only <b>two</b> players may
///      search: the destroyed land's controller, then the activator
///      ("you"). If the destroy half is illegal (no land destroyed) there
///      is no "that land's controller", so only the activator tutors.
///
/// ## Implemented (v1)
/// - Land identity (no printed subtypes).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1).
/// - <b>{2}, {T}, Sacrifice this land: Destroy target nonbasic land an
///   opponent controls.</b> — an <see cref="ActivatedAbility"/> with:
///     - <see cref="ManaCostCost"/> {2}
///     - <see cref="AdditionalCost.Tap"/>
///     - Self-sacrifice inlined in the resolution closure (Wasteland /
///       Field of Ruin posture, since <see cref="AdditionalCost.Sacrifice"/>'s
///       zone-move primitive is still a stub).
///   A 1..1 <see cref="TargetRequest"/> declares "target nonbasic land an
///   opponent controls"; the resolution body gates on (a) Land type,
///   (b) NOT Basic supertype, (c) on the battlefield, (d) controlled by a
///   player other than the activator (CR 608.2b — illegal target → that
///   half does nothing).
/// - <b>Tutor riders</b> (CR 701.19a search / CR 701.20a shuffle). The
///   destroyed land's controller tutors first, then the activator. Each
///   is a "may" search routed through the player's
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>; declining / an
///   empty library still triggers the shuffle. If a single player is both
///   the activator and the destroyed land's controller they cannot be
///   (the target must be an opponent's land), so the two tutors are always
///   distinct players in a legal activation.
///
/// ## Deferred (v1 gaps — shared with Field of Ruin)
/// - <b>AdditionalCost.Sacrifice</b>: self-sac payment is inlined into the
///   resolution closure until the shared primitive ships a zone-move
///   side-effect.
/// - <b>Agent target legality filtering</b>: ActionValidator does not yet
///   narrow the candidate pool to nonbasic-opponent-lands; resolution-time
///   guards catch illegal picks.
/// - <b>Reveal event</b>: same gap as Cultivate / Field of Ruin — the
///   tutored land moves Library → Battlefield without a reveal event.
/// </summary>
[CardName("Demolition Field")]
public static class DemolitionFieldFactory
{
    public const string CardName = "Demolition Field";

    // Basic land names per CR 305.6.
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    /// <summary>Construct Demolition Field owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
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
        // {2}, {T}, Sacrifice this land: Destroy target nonbasic land an
        // opponent controls. That land's controller may tutor a basic;
        // then you may tutor a basic.
        // ----------------------------------------------------------------
        ActivatedAbility? destroyAbility = null;
        var destroyEffect = new Effect(
            $"{CardName}: destroy target nonbasic land an opponent controls; that land's controller then you may tutor a basic to battlefield",
            () =>
            {
                if (destroyAbility == null) return;

                // Self-sacrifice — Field of Ruin posture
                // (AdditionalCost.Sacrifice is a stub today; the cost was
                // declared at activation, the visible zone-move catches up
                // here ahead of the destroy / tutor steps).
                SacrificeToOwnersGraveyard(land);

                // Destroy half — gate the chosen target (CR 608.2b —
                // illegal target → that half does nothing for that target;
                // the activator's tutor still runs).
                Player? destroyedLandController = null;
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
                    destroyedLandController = target.Controller;
                    DestroyToOwnersGraveyard(target);
                }

                // Tutor riders in printed order: "that land's controller"
                // first (only if a land was actually destroyed), then
                // "you" (the activator). Each is a separate "may" search.
                if (destroyedLandController != null)
                {
                    TutorBasicLandToBattlefield(destroyedLandController);
                }

                TutorBasicLandToBattlefield(land.Controller ?? owner);
            });

        destroyAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
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
    /// via <see cref="ZoneService"/> when wired (so ETB triggers fire),
    /// then shuffle the library (CR 701.20a). Declined / empty-candidate
    /// searches still trigger the shuffle.
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

        // CR 701.20a — a search effect shuffles the searched library even
        // if nothing was found.
        LibraryShuffle.ShuffleLibrary(player, "Demolition Field tutor");
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
