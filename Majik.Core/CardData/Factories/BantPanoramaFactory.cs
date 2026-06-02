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
/// Bant Panorama — Alara Reborn / Conflux-era "Panorama" land. The Bant
/// (GWU) member of the five-card paid-fetch Panorama cycle.
///
/// Oracle (verified against Scryfall 2026-06-02):
///   <c>{T}: Add {C}.</c>
///   <c>{1}, {T}, Sacrifice this land: Search your library for a basic Forest,
///      Plains, or Island card, put it onto the battlefield tapped, then
///      shuffle.</c>
///
/// The exact Bountiful Landscape / Twisted Landscape "Landscape" shape MINUS
/// the Cycling clause and with two riders flipped:
/// - the searched basic subtypes are Forest / Plains / Island (Bant's GWU
///   colour identity) rather than the Landscape trio, and
/// - the fetch costs an extra generic <c>{1}</c> (CR 117.5) on top of the
///   <c>{T}, Sacrifice this land</c> the Landscapes / Evolving Wilds pay.
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
///   <see cref="BountifulLandscapeFactory"/> / AetherHub / Urza's Saga. Built
///   from the embedded JSON definition.
/// - <b>{1}, {T}, Sacrifice this land: tutor a basic Forest/Plains/Island onto
///   the battlefield tapped, then shuffle</b> — the Bountiful Landscape /
///   Evolving Wilds idiom narrowed to those three basic subtypes (CR 205.4a —
///   Basic supertype + one of the named land subtypes), plus the printed
///   generic <c>{1}</c> via a <see cref="ManaCostCost"/> in the cost list
///   (CR 117.5). <see cref="AdditionalCost.Tap"/> is the declared tap cost so
///   the ability's <c>CanPay</c> gate reads correctly; the self-sacrifice is
///   inlined in the resolve closure (same posture as
///   <see cref="BountifulLandscapeFactory"/> /
///   <see cref="EvolvingWildsFactory"/>, because
///   <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub) and happens
///   before the search so this land is off the battlefield during the tutor.
///   The Library → Battlefield move routes through
///   <see cref="ZoneServiceRegistry"/> when a live service is registered so
///   ETB replacements / CardMovedEvent subscribers fire on the fetched basic;
///   the printed "tapped" rider is applied after the move.
///
/// ## Deferred (matches every tutor factory)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event.
/// </summary>
[CardName("Bant Panorama")]
public static class BantPanoramaFactory
{
    public const string CardName = "Bant Panorama";
    public const string Slug = "bant-panorama";

    /// <summary>Construct Bant Panorama owned and controlled by
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

        // {1}, {T}, Sacrifice this land: search for a basic Forest/Plains/Island,
        // put it onto the battlefield tapped, then shuffle. CR 205.4a / CR 117.5.
        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{CardName}: sacrifice self + tutor basic Forest/Plains/Island -> battlefield tapped, shuffle",
            async ctx =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice (CR 701.16) before the search so this land is
                // no longer on the battlefield during the tutor.
                SacrificeToOwnersGraveyard(land);

                await TutorBasicForestPlainsIslandToBattlefieldTappedAsync(controller, ctx)
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
    /// subtype is Forest, Plains, or Island (CR 205.4a — Basic supertype +
    /// one of those land subtypes), consult the agent to pick among candidates
    /// (falls back to the first deterministic match), move the chosen card to
    /// the battlefield, apply the printed "tapped" rider, then shuffle
    /// (CR 701.20a — shuffle whether or not a card was found).
    /// </summary>
    private static async ValueTask TutorBasicForestPlainsIslandToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land)
                        && c.HasSupertype(CardSupertype.Basic)
                        && (c.HasSubtype(CardSubtype.Forest)
                            || c.HasSubtype(CardSubtype.Plains)
                            || c.HasSubtype(CardSubtype.Island)))
            .ToList();

        // CR 701.19a — prompt agent even on zero candidates so the human
        // searcher sees the failed search rather than a silent no-op.
        var pick = await LibrarySearch.PromptOnlyAsync(ctx, player, candidates, "basic Forest, Plains, or Island card")
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
        LibraryShuffle.ShuffleLibrary(player, "bant-panorama");
    }
}
