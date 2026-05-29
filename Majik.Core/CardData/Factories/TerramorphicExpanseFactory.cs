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
/// Terramorphic Expanse — Time Spiral land; a functional reprint of
/// Evolving Wilds.
///
/// Oracle (Scryfall, verified): <c>{T}, Sacrifice this land: Search your
/// library for a basic land card, put it onto the battlefield tapped, then
/// shuffle.</c>
///
/// Same sac-to-fetch shape as <see cref="PrismaticVistaFactory"/> /
/// <see cref="WayfarersBaubleFactory"/>, but the fetched basic enters
/// <b>tapped</b> (printed rider) and there is no life payment and no mana
/// component in the activation cost — only <c>{T}</c> + self-sacrifice.
///
/// Card identity (a nonbasic Land with no supertype/subtype, producing no
/// mana on its own; CR 305.6) is loaded from
/// <c>Majik.Core/CardData/Cards/terramorphic-expanse.json</c> via
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="SmolderingMarshFactory"/>. The non-mana activated fetch
/// ability is then attached in code because the JSON
/// <see cref="AbilityDefinition"/> schema does not yet model
/// search/sacrifice/enters-tapped tutor abilities (it currently covers mana
/// abilities only), matching the established sac-fetch land factories
/// (Prismatic Vista, Wasteland, Strip Mine, Ghost Quarter).
///
/// ## Implemented (v1)
/// - Land identity (no supertype, no subtypes — produces no mana on its
///   own; CR 305.6).
/// - Activated ability: <c>{T}, Sacrifice this land:</c> search the
///   controller's library for a basic land card (CR 205.4a — Basic
///   supertype + Land card type), put it onto the battlefield tapped, then
///   shuffle.
/// - Self-sacrifice inlined in the resolve closure (same trick as
///   <see cref="PrismaticVistaFactory"/> / <see cref="WastelandFactory"/>)
///   because <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub.
/// - <see cref="AdditionalCost.Tap"/> is the declared cost so the ability's
///   <c>CanPay</c> gate still reads correctly.
/// - Library → Battlefield routed through <see cref="ZoneServiceRegistry"/>
///   so ETB-tapped replacements (e.g. snow basics) and CardMovedEvent
///   subscribers (Amulet of Vigor untap, bounce-land ETB triggers) fire;
///   the tutored basic is then tapped per the printed rider (CR 305 /
///   614 — same posture as <see cref="WayfarersBaubleFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the generic
///   <see cref="AdditionalCost"/> sacrifice payment is a no-op stub; the
///   closure performs the zone move directly so behaviour is observable —
///   same posture as Prismatic Vista / Wayfarer's Bauble.
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event. Same gap as every tutor factory.
/// </summary>
[CardName("Terramorphic Expanse")]
public static class TerramorphicExpanseFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("terramorphic-expanse");

    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            "Terramorphic Expanse: sac self + tutor basic land -> battlefield tapped, shuffle",
            () =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice — move this land from battlefield to its
                // owner's graveyard (CR 701.16). Must happen before the
                // library search so the land is no longer in the library.
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
    /// move the chosen card to the battlefield, tap it (printed rider; CR
    /// 305 / 614), then shuffle (CR 701.20a).
    /// </summary>
    private static void TutorBasicLandToBattlefieldTapped(Player player)
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
        }

        // CR 701.20a — shuffle whether or not a card was found.
        LibraryShuffle.ShuffleLibrary(player, "terramorphic-expanse");
    }
}
