using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Bountiful Landscape — Streets of New Capenna land (the exact New Capenna
/// twin of <see cref="TwistedLandscapeFactory"/>, with the basics and cycling
/// color identity swapped).
///
/// Oracle (verified against Scryfall):
///   <c>{T}: Add {C}.</c>
///   <c>{T}, Sacrifice this land: Search your library for a basic Forest,
///      Island, or Mountain card, put it onto the battlefield tapped, then
///      shuffle.</c>
///   <c>Cycling {G}{U}{R}</c>
///
/// Built imperatively (not via <see cref="Definitions.CardDefinitionFactory"/>)
/// because the JSON definition schema does not yet express either the
/// search-library-for-basic-onto-battlefield-tapped effect or the Cycling
/// keyword. Composes three idioms already in the engine:
///
/// ## Implemented
/// - <b>Land identity</b> — no supertype, no subtypes (the colorless mana is
///   declared inline; CR 305.7).
/// - <b>{T}: Add {C}</b> — vanilla <see cref="ManaAbility"/> (CR 605.1 — mana
///   ability, no stack). {C} is colorless mana (CR 107.4c) modeled as +1
///   generic in the engine's <see cref="ManaCost"/>, the same posture as
///   <see cref="AetherHubFactory"/> / Urza's Saga.
/// - <b>{T}, Sacrifice this land: tutor a basic Forest/Island/Mountain onto the
///   battlefield tapped, then shuffle</b> — the Evolving Wilds idiom
///   (<see cref="EvolvingWildsFactory"/>) narrowed to those three basic
///   subtypes (CR 205.4a — Basic supertype + one of the named land subtypes).
///   <see cref="AdditionalCost.Tap"/> is the declared cost so the ability's
///   <c>CanPay</c> gate reads correctly; the self-sacrifice is inlined in the
///   resolve closure (same posture as <see cref="EvolvingWildsFactory"/>,
///   because <see cref="AdditionalCost.Sacrifice"/>.Pay() is a no-op stub) and
///   happens before the search so this land is off the battlefield during the
///   tutor. The Library → Battlefield move routes through
///   <see cref="ZoneServiceRegistry"/> when a live service is registered so
///   ETB replacements / CardMovedEvent subscribers fire on the fetched basic;
///   the printed "tapped" rider is applied after the move.
/// - <b>Cycling {G}{U}{R}</b> (CR 702.32) — wired through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>(<c>{G}{U}{R}</c>). When a bus is supplied the
///   cycling resolve publishes <see cref="CardCycledEvent"/> (CR 702.32d).
///
/// ## Deferred (matches every tutor factory)
/// - <b>Reveal event</b>: the tutored basic moves Library → Battlefield
///   without publishing a reveal event.
/// </summary>
[CardName("Bountiful Landscape")]
public static class BountifulLandscapeFactory
{
    public const string CardName = "Bountiful Landscape";

    /// <summary>Construct Bountiful Landscape owned and controlled by
    /// <paramref name="owner"/> with no bus wiring (cycling does not publish
    /// <see cref="CardCycledEvent"/>).</summary>
    public static Land Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Bountiful Landscape owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    /// <param name="owner">The player who owns and initially controls the land.</param>
    /// <param name="eventBus">Optional event bus the cycling resolve publishes
    /// <see cref="CardCycledEvent"/> against (CR 702.32d).</param>
    public static Land Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(CardName, supertypes: null, subtypes: null);
        land.SetOwner(owner);
        land.SetController(owner);

        // {T}: Add {C}. CR 605.1 — mana ability (no stack). {C} = colorless
        // (CR 107.4c), modeled as +1 generic — same posture as AetherHub.
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("C")));

        // {T}, Sacrifice this land: search for a basic Forest/Island/Mountain,
        // put it onto the battlefield tapped, then shuffle. CR 205.4a.
        ActivatedAbility? fetchAbility = null;
        var fetchEffect = new Effect(
            $"{CardName}: sacrifice self + tutor basic Forest/Island/Mountain -> battlefield tapped, shuffle",
            async ctx =>
            {
                if (fetchAbility == null) return;

                var controller = land.Controller ?? land.Owner;
                if (controller == null) return;

                // Self-sacrifice (CR 701.16) before the search so this land is
                // no longer on the battlefield during the tutor.
                SacrificeToOwnersGraveyard(land);

                await TutorBasicForestIslandMountainToBattlefieldTappedAsync(controller, ctx)
                    .ConfigureAwait(false);
            });

        fetchAbility = new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(land) },
            effects: new IEffect[] { fetchEffect });

        land.AddAbility(fetchAbility);

        // Cycling {G}{U}{R}. CR 702.32 — the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the CardCycledEvent
        // publish (CR 702.32d).
        CyclingFactory.Build(land, new ManaCostCost("GUR"), eventBus);

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
    /// subtype is Forest, Island, or Mountain (CR 205.4a — Basic supertype +
    /// one of those land subtypes), consult the agent to pick among candidates
    /// (falls back to the first deterministic match), move the chosen card to
    /// the battlefield, apply the printed "tapped" rider, then shuffle
    /// (CR 701.20a — shuffle whether or not a card was found).
    /// </summary>
    private static async ValueTask TutorBasicForestIslandMountainToBattlefieldTappedAsync(Player player, ResolutionContext ctx)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land)
                        && c.HasSupertype(CardSupertype.Basic)
                        && (c.HasSubtype(CardSubtype.Forest)
                            || c.HasSubtype(CardSubtype.Island)
                            || c.HasSubtype(CardSubtype.Mountain)))
            .ToList();

        // CR 701.19a — prompt agent even on zero candidates so the human
        // searcher sees the failed search rather than a silent no-op.
        var pick = await LibrarySearch.PromptOnlyAsync(ctx, player, candidates, "basic Forest, Island, or Mountain card")
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
        LibraryShuffle.ShuffleLibrary(player, "bountiful-landscape");
    }
}
