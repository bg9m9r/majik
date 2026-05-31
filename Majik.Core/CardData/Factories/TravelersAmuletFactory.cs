using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Traveler's Amulet (Lorwyn / reprints, {1}).
///
/// Artifact. Oracle text:
///   "{1}, Sacrifice this artifact: Search your library for a basic land
///    card, reveal it, put it into your hand, then shuffle."
///
/// ## Shape source
/// Card identity (name, {1}, Artifact) is loaded from
/// <c>Majik.Core/CardData/Cards/travelers-amulet.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The single activated ability is
/// attached in code below: the JSON ability schema does not yet express a
/// "search for a basic land → hand → shuffle" effect, so it is hand-rolled
/// here — same posture as <see cref="ExpeditionMapFactory"/> (tutor a land to
/// hand) and <see cref="BorderlandRangerFactory"/> (basic-only, to hand).
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{1}, Sacrifice ~: tutor a basic land to hand</b> — single
///   <see cref="ActivatedAbility"/> with two costs:
///   <see cref="ManaCostCost"/>("{1}") + <see cref="AdditionalCost.Sacrifice"/>
///   on the amulet itself. There is NO {T} cost — the printed line is just
///   "{1}, Sacrifice this artifact:" (this is what distinguishes it from
///   Expedition Map's "{1}, {T}, Sacrifice"). Same cost stack as Pyrite
///   Spellbomb's "{R}, Sacrifice" line (CR 602 — activated ability).
/// - Resolution sacrifices the amulet (battlefield → owner's graveyard,
///   CR 701.16), then searches the controller's library for ONE basic land
///   card (CR 305.6 — Basic supertype + Land card type), consults the
///   registered <see cref="IPlayerAgent"/> via
///   <see cref="LibrarySearch.PromptOnly"/> (CR 701.19a — the agent may decline;
///   deterministic first-basic fallback when no agent is registered — same
///   posture as Expedition Map / Borderland Ranger), moves the pick
///   Library → Hand, then shuffles ONCE via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a — a single search
///   effect performs one shuffle whether or not a card was found).
/// - Basic-only predicate (CR 305.6) restricts candidates to the Basic
///   supertype + Land card type, so nonbasic lands are NOT tutorable — the
///   distinguishing difference from Expedition Map's "any land".
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the picked basic moves Library → Hand
///   without publishing a reveal event. Same gap as Expedition Map /
///   Borderland Ranger / every tutor factory. The card still reaches the hand,
///   so the observable game state is correct; only the "reveal" UI signal is
///   absent.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op stub.
///   The effect closure performs the zone move directly so behaviour is
///   observable — same posture as Expedition Map / Pyrite Spellbomb / Mind
///   Stone. Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// </summary>
[CardName("Traveler's Amulet")]
public static class TravelersAmuletFactory
{
    public const string CardName = "Traveler's Amulet";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("travelers-amulet");

    /// <summary>
    /// Construct Traveler's Amulet owned and controlled by
    /// <paramref name="owner"/>. The single "{1}, Sac: tutor a basic land to
    /// hand" activated ability is attached structurally.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var amulet = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        amulet.SetOwner(owner);
        amulet.SetController(owner);

        // ----------------------------------------------------------------
        // {1}, Sacrifice this artifact: Search your library for a basic land
        // card, reveal it, put it into your hand, then shuffle.
        // CR 602 — activated ability with two costs (mana + sac; no {T}).
        // CR 305.6 — basic land = Basic supertype + Land card type.
        // CR 701.19a — search consults the agent (null = decline; legal).
        // CR 701.20a — shuffle once after the search via LibraryShuffle.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            "Traveler's Amulet: tutor a basic land -> hand + sac self",
            async ctx =>
            {
                var controller = amulet.Controller ?? owner;
                SacrificeSelf(amulet, owner, controller);

                var candidates = controller.Zones.Library.GetCards()
                    .Where(IsBasicLand)
                    .ToList();

                // CR 701.19a — prompt the agent even on zero candidates so a
                // human searcher sees the failed search.
                var pick = await LibrarySearch.PromptOnlyAsync(
                    ctx, controller, candidates, "basic land card").ConfigureAwait(false);

                if (pick != null)
                {
                    controller.Zones.Library.RemoveCard(pick);
                    controller.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }

                // CR 701.20a — shuffle whether or not a card was found.
                LibraryShuffle.ShuffleLibrary(controller, "travelers-amulet");
            });

        var tutorAbility = new ActivatedAbility(
            source: amulet,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Sacrifice(amulet),
            },
            effects: new IEffect[] { tutorEffect });

        amulet.AddAbility(tutorAbility);

        return amulet;
    }

    /// <summary>
    /// CR 305.6 — a basic land card has the Basic supertype and the Land card
    /// type. Nonbasic lands (e.g. Urza's Tower) are excluded.
    /// </summary>
    private static bool IsBasicLand(ICard c) =>
        c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

    /// <summary>
    /// CR 701.16 — move <paramref name="amulet"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by Expedition
    /// Map / Mind Stone / Pyrite Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact amulet, Player owner, Player controller)
    {
        if (amulet.Zone != ZoneType.Battlefield) return;
        controller.Zones.Battlefield.RemoveCard(amulet);
        owner.Zones.Graveyard.AddCard(amulet);
        amulet.SetZone(ZoneType.Graveyard);
    }
}
