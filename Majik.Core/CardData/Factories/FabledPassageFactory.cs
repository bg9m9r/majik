using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Fabled Passage — Throne of Eldraine (and reprints) land.
///
/// Oracle (Scryfall, verified): <c>{T}, Sacrifice this land: Search your
/// library for a basic land card, put it onto the battlefield tapped, then
/// shuffle. Then if you control four or more lands, untap that land.</c>
///
/// This is <see cref="TerramorphicExpanseFactory"/> / <see cref="EvolvingWildsFactory"/>
/// (search a basic land, put it onto the battlefield <b>tapped</b>, then
/// shuffle) plus one printed rider: after the fetch, if the controller controls
/// four or more lands, untap the just-fetched land. The fetched land counts
/// toward that four — the "four or more lands" check happens after it has
/// entered the battlefield — whereas the sacrificed Fabled Passage does not
/// (it left the battlefield as part of paying the cost).
///
/// Card identity (a nonbasic Land with no supertype/subtype, producing no mana
/// on its own; CR 305.6) is loaded from
/// <c>Majik.Core/CardData/Cards/fabled-passage.json</c> via
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="TerramorphicExpanseFactory"/>. The non-mana activated fetch
/// ability is then attached in code because the JSON
/// <see cref="AbilityDefinition"/> schema does not model
/// search/sacrifice/enters-tapped tutor abilities (it currently covers mana
/// abilities only).
///
/// ## Implemented (v1)
/// - Land identity (no supertype, no subtypes — produces no mana on its own;
///   CR 305.6).
/// - Activated ability: <c>{T}, Sacrifice this land:</c> search the
///   controller's library for a basic land card (CR 205.4a — Basic supertype
///   + Land card type), put it onto the battlefield tapped, then shuffle.
/// - Printed rider: after the fetch, if the controller controls four or more
///   lands (counting the just-fetched land, which has already entered the
///   battlefield), untap that land. The sacrificed Fabled Passage is no
///   longer on the battlefield and does not count.
/// - Self-sacrifice inlined in the resolve closure (same trick as
///   <see cref="TerramorphicExpanseFactory"/> / <see cref="PrismaticVistaFactory"/>)
///   because <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub.
///   The sacrifice happens before the search so the land is no longer in the
///   library/battlefield during the tutor (and so it does not inflate the
///   four-or-more-lands count).
/// - <see cref="AdditionalCost.Tap"/> is the declared cost so the ability's
///   <c>CanPay</c> gate still reads correctly.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/>
///   so ETB-tapped replacements (e.g. snow basics) and CardMovedEvent
///   subscribers (Amulet of Vigor untap, bounce-land ETB triggers) fire on
///   the tutored basic; the printed "tapped" rider is then applied (and, when
///   the four-or-more-lands condition holds, immediately reversed by the untap
///   rider) per CR 305 / 614.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the generic
///   <see cref="AdditionalCost"/> sacrifice payment is a no-op stub; the
///   closure performs the zone move directly so behaviour is observable —
///   same posture as Terramorphic Expanse / Prismatic Vista.
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Fabled Passage")]
public static class FabledPassageFactory
{
    /// <summary>Untap the fetched land when the controller has at least this many lands.</summary>
    private const int UntapLandThreshold = 4;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("fabled-passage");

    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            "Fabled Passage: sac self + tutor basic land -> battlefield tapped, shuffle, untap if 4+ lands",
            () =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice — move this land from battlefield to its
                // owner's graveyard (CR 701.16). Must happen before the
                // library search so the land is no longer in the library and
                // so it does not count toward the four-or-more-lands rider.
                SacrificeToOwnersGraveyard(land);

                TutorBasicLandThenMaybeUntap(controller);
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
    /// move the chosen card to the battlefield, tap it (printed rider; CR 305 /
    /// 614), then shuffle (CR 701.20a). Finally, if the controller now controls
    /// four or more lands (the fetched land included), untap that land.
    /// </summary>
    private static void TutorBasicLandThenMaybeUntap(Player player)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic))
            .ToList();

        // CR 701.19a — prompt the agent even on zero candidates so the human
        // searcher sees the failed search rather than a silent no-op.
        var pick = LibrarySearch.PromptOnly(
            player, candidates, "basic land card");

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

            // Printed rider: "Then if you control four or more lands, untap
            // that land." The just-fetched land is already on the battlefield
            // and counts toward the total; Fabled Passage itself was
            // sacrificed and no longer counts.
            var landCount = player.Zones.Battlefield.GetCards()
                .Count(c => c.HasType(CardType.Land));
            if (landCount >= UntapLandThreshold && pick is Permanent permUntap && permUntap.IsTapped)
            {
                permUntap.Untap();
            }
        }

        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(player, "fabled-passage");
    }
}
