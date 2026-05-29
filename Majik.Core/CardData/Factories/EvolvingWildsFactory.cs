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
/// Evolving Wilds — Conflux (and many reprints) land.
///
/// Oracle: <c>{T}, Sacrifice Evolving Wilds: Search your library for a basic
/// land card, put it onto the battlefield tapped, then shuffle.</c>
///
/// Identical tutor shape to <see cref="WayfarersBaubleFactory"/> (search a
/// basic land, put it onto the battlefield <b>tapped</b>, then shuffle) but
/// printed as a Land whose only cost is <c>{T}</c> + self-sacrifice — no mana
/// and no life payment. Equivalently it is <see cref="PrismaticVistaFactory"/>
/// minus the "Pay 1 life" rider, plus the printed "tapped" rider on the
/// fetched basic.
///
/// ## Implemented (v1)
/// - Land identity (no supertype, no subtypes — produces no mana on its own;
///   CR 305.7).
/// - Activated ability: <c>{T}, Sacrifice this land:</c> search the
///   controller's library for a basic land card (CR 205.4a — Basic supertype
///   + Land card type), put it onto the battlefield tapped, then shuffle.
/// - Self-sacrifice inlined in the resolve closure (same posture as
///   <see cref="PrismaticVistaFactory"/> / <see cref="WayfarersBaubleFactory"/>)
///   because <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub.
///   The sacrifice happens before the search so the land is no longer in the
///   library/battlefield during the tutor.
/// - <see cref="AdditionalCost.Tap"/> is the declared cost so the ability's
///   <c>CanPay</c> gate still reads correctly.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/>
///   so ETB-tapped replacements (e.g. snow basics) and CardMovedEvent
///   subscribers (Amulet of Vigor untap, Lotus Cobra) fire on the tutored
///   basic; the printed "tapped" rider is applied after the move.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Evolving Wilds")]
public static class EvolvingWildsFactory
{
    public const string CardName = "Evolving Wilds";

    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{CardName}: sacrifice self + tutor basic land -> battlefield tapped, shuffle",
            () =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice — move this land from battlefield to owner's
                // graveyard (CR 701.16). Must happen before the library search
                // so the land is no longer on the battlefield during the tutor.
                SacrificeToOwnersGraveyard(land);

                TutorBasicLandToBattlefieldTapped(controller);
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
    /// move the chosen card to the battlefield, apply the printed "tapped"
    /// rider, then shuffle (CR 701.20a — shuffle whether or not a card was
    /// found).
    /// </summary>
    private static void TutorBasicLandToBattlefieldTapped(Player player)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();

        // CR 701.19a — prompt agent even on zero candidates so the human
        // searcher sees the failed search rather than a silent no-op.
        var pick = LibrarySearch.PromptOnly(player, candidates, "basic land card");

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
                if (pick is Permanent permTapped && !permTapped.IsTapped)
                {
                    permTapped.Tap();
                }
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
                if (pick is Permanent perm)
                {
                    perm.Tap();
                }
            }
        }

        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(player, "evolving-wilds");
    }
}
