using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Renegade Map (Aether Revolt, {1}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "This artifact enters tapped.
///    {T}, Sacrifice this artifact: Search your library for a basic land card,
///    reveal it, put it into your hand, then shuffle."
///
/// The card's base shape (name, single Artifact card type, {1}) is materialised
/// from the embedded JSON definition (<c>renegade-map.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="PilgrimsEyeFactory"/> / <see cref="AbradedBluffsFactory"/>. The
/// {T}, Sacrifice search-to-hand ability is layered on here because the JSON
/// schema doesn't express search effects.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{T}, Sacrifice ~: tutor a basic land to hand</b> — single
///   <see cref="ActivatedAbility"/> with two costs:
///   <see cref="AdditionalCost.Tap"/> on the map +
///   <see cref="AdditionalCost.Sacrifice"/> on the map itself. There is NO
///   mana pip in the printed cost (unlike Expedition Map's {1}, {T}, Sac).
///   Resolution sacrifices the map (battlefield → owner's graveyard,
///   CR 701.16), searches the controller's library for ONE basic land card
///   (CR 305.6 — Basic supertype + Land card type), consults the agent via
///   <see cref="LibrarySearch.PromptOnly"/> (CR 701.19a — declining is legal;
///   deterministic first-basic fallback when no agent registered — same
///   posture as <see cref="PilgrimsEyeFactory"/> / <see cref="ExpeditionMapFactory"/>),
///   moves the pick Library → Hand, then shuffles once
///   (CR 701.20a) whether or not a card was found.
/// - <b>Enters-tapped (CR 614.1c)</b> — unconditional
///   "This artifact enters tapped." Applied on the production load path by
///   <see cref="EntersTappedBinder"/> from the oracle text (this factory
///   builds the artifact without it — same posture as
///   <see cref="AbradedBluffsFactory"/> — so the replacement isn't
///   double-registered; the binder owns it).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the tutored basic moves Library → Hand
///   without publishing a reveal event. Same gap as Expedition Map /
///   Pilgrim's Eye / every tutor-to-hand factory.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — same posture as Expedition Map / Pyrite Spellbomb.
///   Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// </summary>
[CardName("Renegade Map")]
public static class RenegadeMapFactory
{
    public const string CardName = "Renegade Map";
    public const string Slug = "renegade-map";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Renegade Map owned and controlled by
    /// <paramref name="owner"/>. The single "{T}, Sacrifice: tutor a basic
    /// land to hand" activated ability is attached structurally. The
    /// enters-tapped replacement (CR 614.1c) is owned by
    /// <see cref="EntersTappedBinder"/> on the production load path, not here.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Artifact, {1}) from the embedded JSON definition.
        var map = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        map.SetOwner(owner);
        map.SetController(owner);

        // ----------------------------------------------------------------
        // {T}, Sacrifice this artifact: Search your library for a basic land
        // card, reveal it, put it into your hand, then shuffle.
        // CR 602 — activated ability with two costs (tap + sac), no mana.
        // CR 701.19a — search consults the agent (null = decline; legal).
        // CR 701.20a — shuffle once after the search.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: tutor a basic land -> hand + sac self",
            () =>
            {
                var controller = map.Controller ?? owner;
                SacrificeSelf(map, owner, controller);
                TutorOneBasicToHand(controller);
            });

        var tutorAbility = new ActivatedAbility(
            source: map,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(map),
                AdditionalCost.Sacrifice(map),
            },
            effects: new IEffect[] { tutorEffect });

        map.AddAbility(tutorAbility);

        return map;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="map"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by
    /// Expedition Map / Pyrite Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact map, Player owner, Player controller)
    {
        if (map.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(map);
        owner.Zones.Graveyard.AddCard(map);
        map.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent
    /// (which may decline — deterministic first-basic fallback when no
    /// agent), move the pick Library → Hand (CR 701.19a), then shuffle once
    /// (CR 701.20a) whether or not a card was found. Mirrors
    /// <see cref="PilgrimsEyeFactory"/>'s to-hand basic tutor.
    /// </summary>
    private static void TutorOneBasicToHand(Player player)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();

        // CR 701.19a — prompt even on zero candidates so the human searcher
        // sees the failed search.
        var pick = LibrarySearch.PromptOnly(
            player, candidates, "basic land card to put into your hand");

        if (pick != null)
        {
            player.Zones.Library.RemoveCard(pick);
            player.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, Slug);
    }
}
