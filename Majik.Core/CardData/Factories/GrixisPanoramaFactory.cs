using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Grixis Panorama — Alara Reborn / Conflux-era "Panorama" land. The Grixis
/// (UBR) member of the five-card paid-fetch "Panorama" cycle.
///
/// Oracle (verified against Scryfall 2026-06-02):
///   <c>{T}: Add {C}.</c>
///   <c>{1}, {T}, Sacrifice this land: Search your library for a basic Island,
///      Swamp, or Mountain card, put it onto the battlefield tapped, then
///      shuffle.</c>
///
/// The exact <see cref="BantPanoramaFactory"/> shape with the searched basic
/// subtypes swapped to Island / Swamp / Mountain (Grixis's UBR colour
/// identity) instead of Forest / Plains / Island.
///
/// Built imperatively (not via <see cref="Definitions.CardDefinitionFactory"/>)
/// because the JSON definition schema does not yet express the
/// search-library-for-basic-onto-battlefield-tapped effect; the colourless
/// mana ability is still materialised from the embedded JSON definition so the
/// card's base mana surface is data-driven (same posture as the other named
/// land factories). Composes idioms already in the engine:
///
/// ## Implemented
/// - <b>Land identity</b> — no supertype, no subtypes (Panoramas are plain
///   nonbasic lands; CR 305.7). Comes from the JSON definition.
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1 — mana
///   ability, no stack). {C} is colorless mana (CR 107.4c) modeled as +1
///   generic in the engine's <see cref="ManaCost"/>, the same posture as
///   <see cref="BantPanoramaFactory"/> / AetherHub / Urza's Saga. Built from
///   the embedded JSON definition.
/// - <b>{1}, {T}, Sacrifice this land: tutor a basic Island/Swamp/Mountain onto
///   the battlefield tapped, then shuffle</b> — the Bant Panorama / Evolving
///   Wilds idiom narrowed to those three basic subtypes (CR 205.4a — Basic
///   supertype + one of the named land subtypes), plus the printed generic
///   <c>{1}</c> via a <see cref="ManaCostCost"/> in the cost list (CR 117.5).
///   <see cref="AdditionalCost.Tap"/> is the declared tap cost so the
///   ability's <c>CanPay</c> gate reads correctly; the self-sacrifice is
///   inlined in the resolve closure (same posture as
///   <see cref="BantPanoramaFactory"/> / <see cref="EvolvingWildsFactory"/>,
///   because <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub) and
///   happens before the search so this land is off the battlefield during the
///   tutor. The Library → Battlefield move routes through
///   <see cref="ZoneServiceRegistry"/> when a live service is registered so
///   ETB replacements / CardMovedEvent subscribers fire on the fetched basic;
///   the printed "tapped" rider is applied after the move.
///
/// ## Deferred (matches every tutor factory)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event.
/// </summary>
[CardName("Grixis Panorama")]
public static class GrixisPanoramaFactory
{
    public const string CardName = "Grixis Panorama";
    public const string Slug = "grixis-panorama";

    /// <summary>Construct Grixis Panorama owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Land type, and
        // the {T}: Add {C} mana ability). The {1}{T}Sacrifice fetch is layered
        // on below — it is not expressible in the current JSON
        // AbilityDefinition schema.
        var definition = Definitions.CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)Definitions.CardDefinitionFactory.Build(definition, owner);

        // {1}, {T}, Sacrifice this land: search for a basic Island/Swamp/Mountain,
        // put it onto the battlefield tapped, then shuffle. CR 205.4a / CR 117.5.
        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{CardName}: sacrifice self + tutor basic Island/Swamp/Mountain -> battlefield tapped, shuffle",
            async ctx =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice (CR 701.16) before the search so this land is
                // no longer on the battlefield during the tutor.
                SacrificeToOwnersGraveyard(land);

                await TutorBasicIslandSwampMountainToBattlefieldTappedAsync(controller, ctx)
                    .ConfigureAwait(false);
            });

        fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                // CR 117.5 — the printed cost is {1}, {T}, Sacrifice this land.
                new ManaCostCost("1"),
                AdditionalCost.Tap(land),
            },
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
    /// Search <paramref name="player"/>'s library for a basic land card whose
    /// subtype is Island, Swamp, or Mountain (CR 205.4a — Basic supertype +
    /// one of those land subtypes), consult the agent to pick among candidates
    /// (falls back to the first deterministic match), move the chosen card to
    /// the battlefield, apply the printed "tapped" rider, then shuffle
    /// (CR 701.20a — shuffle whether or not a card was found).
    /// </summary>
    private static async ValueTask TutorBasicIslandSwampMountainToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land)
                        && c.HasSupertype(CardSupertype.Basic)
                        && (c.HasSubtype(CardSubtype.Island)
                            || c.HasSubtype(CardSubtype.Swamp)
                            || c.HasSubtype(CardSubtype.Mountain)))
            .ToList();

        // CR 701.19a — prompt agent even on zero candidates so the human
        // searcher sees the failed search rather than a silent no-op.
        var pick = await LibrarySearch.PromptOnlyAsync(ctx, player, candidates, "basic Island, Swamp, or Mountain card")
            .ConfigureAwait(false);

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
        LibraryShuffle.ShuffleLibrary(player, "grixis-panorama");
    }
}
