using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Prismatic Vista — Modern Horizons land.
///
/// Oracle: <c>{T}, Pay 1 life, Sacrifice Prismatic Vista: Search your library
/// for a basic land card, put it onto the battlefield, then shuffle.</c>
///
/// Identical shape to the Onslaught / Zendikar fetchland cycle
/// (see <see cref="FetchLandCycleFactory"/>) but the library predicate
/// restricts to <b>basic</b> lands only (CR 205.4a) — i.e. any card with the
/// Basic supertype and the Land card type. Unlike the colour-pair fetches it
/// does not pick up dual-typed nonbasics, but it pulls in any of the five
/// basics plus Wastes (basic Wastes land if present).
///
/// ## Implemented (v1)
/// - Land identity (no supertype, no subtypes — produces no mana on its own;
///   CR 305.7).
/// - Activated ability: <c>{T}, Pay 1 life, Sacrifice this land:</c>
///   search the controller's library for a basic land card, put it onto
///   the battlefield untapped, then shuffle.
/// - Self-sacrifice + 1-life payment inlined in the resolve closure (same
///   trick as <see cref="FetchLandCycleFactory"/> / <see cref="WastelandFactory"/>)
///   because <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub.
/// - <see cref="AdditionalCost.Tap"/> is the declared cost so the ability's
///   <c>CanPay</c> gate still reads correctly.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/>
///   so ETB-tapped replacements (e.g. snow basics) and CardMovedEvent
///   subscribers (Amulet of Vigor untap, bounce-land ETB triggers) fire.
/// </summary>
[CardName("Prismatic Vista")]
public static class PrismaticVistaFactory
{
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Prismatic Vista", supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            "Prismatic Vista: search library for a basic land, put onto battlefield, shuffle",
            () =>
            {
                if (fetchAbility == null) return;

                // Pay 1 life (CR 119.4).
                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;
                controller.LoseLife(1);

                // Self-sacrifice — move this land from battlefield to
                // owner's graveyard (CR 701.16). Must happen before the
                // library search so the land is no longer in the library.
                SacrificeToOwnersGraveyard(land);

                TutorBasicLandToBattlefield(controller);
            });

        fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { fetchEffect });

        land.AddAbility(fetchAbility);
        return land;
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

    /// <summary>
    /// Search <paramref name="player"/>'s library for a basic land card
    /// (CR 205.4a — Basic supertype + Land card type), consult the agent to
    /// pick among candidates (falls back to the first deterministic match),
    /// move the chosen card to the battlefield untapped (CR 305), then
    /// shuffle (CR 701.20a).
    /// </summary>
    private static void TutorBasicLandToBattlefield(Player player)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();
        if (candidates.Count == 0)
        {
            // CR 701.20a — still shuffle even when search finds nothing.
            Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "prismatic-vista");
            return;
        }

        var agent = AgentRegistry.Get(player);
        ICard? pick = agent != null
            ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "basic land card")
                .GetAwaiter().GetResult()
            : candidates[0];
        if (pick == null)
        {
            Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "prismatic-vista");
            return;
        }

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
            pick.SetController(player);
        }

        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(player, "prismatic-vista");
    }
}
