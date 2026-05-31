using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Expedition Map (Worldwake, {1}).
///
/// Artifact. Oracle text:
///   "{1}, {T}, Sacrifice Expedition Map: Search your library for a land
///    card, reveal it, put it into your hand, then shuffle."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{1}, {T}, Sacrifice ~: Tutor a land to hand</b> — single
///   <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/>("{1}") + <see cref="AdditionalCost.Tap"/>
///   on the map + <see cref="AdditionalCost.Sacrifice"/> on the map itself.
///   Resolution sacrifices the map (battlefield → owner's graveyard),
///   consults the controller's agent via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> for the land choice
///   (CR 701.19a; deterministic first-land fallback when no agent registered
///   — same posture as Stoneforge Mystic / Sylvan Scrying), moves the pick
///   to hand, and shuffles via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a — publishes <c>LibraryShuffledEvent</c> when a bus is
///   registered).
/// - Tutors ANY land — basic or nonbasic — which is the Tron deck's reason
///   for running it (finds Urza's Mine / Tower / Power Plant). Predicate
///   mirrors <c>SearchSpellFactory.SearchLibrarySpell("land")</c>:
///   <c>c.HasType(CardType.Land)</c>.
/// - Decline-to-find is legal: agent returning null = no-op (CR 701.19a).
///   Empty land pile = clean no-op.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the picked land moves Library → Hand
///   without publishing a reveal event. Same gap as Stoneforge Mystic's
///   ETB tutor / Sylvan Scrying / every tutor factory.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — same posture as Mind Stone / Pyrite Spellbomb /
///   Lotus Petal. Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// </summary>
[CardName("Expedition Map")]
public static class ExpeditionMapFactory
{
    public const string CardName = "Expedition Map";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Expedition Map owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var map = new Artifact(CardName, PrintedManaCost);
        map.SetOwner(owner);
        map.SetController(owner);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice Expedition Map: Search your library for a
        // land card, reveal it, put it into your hand, then shuffle.
        // CR 602 — activated ability with three costs. CR 701.19a —
        // search consults the agent (null = decline; legal).
        // CR 701.20a — shuffle after the search via LibraryShuffle.
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            "Expedition Map: tutor a land -> hand + sac self",
            async ctx =>
            {
                SacrificeSelf(map, owner);

                var candidates = owner.Zones.Library.GetCards()
                    .Where(c => c.HasType(CardType.Land))
                    .ToList();

                // CR 701.19a — prompt agent even on zero candidates so the
                // human searcher sees the failed search (see LibrarySearch
                // xmldoc).
                var pick = await LibrarySearch.PromptOnlyAsync(
                    ctx, owner, candidates, "land card").ConfigureAwait(false);

                if (pick != null)
                {
                    owner.Zones.Library.RemoveCard(pick);
                    owner.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                }

                // CR 701.20a — shuffle whether or not a card was found.
                LibraryShuffle.ShuffleLibrary(owner, "expedition-map");
            });

        var tutorAbility = new ActivatedAbility(
            source: map,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(map),
                AdditionalCost.Sacrifice(map),
            },
            effects: new IEffect[] { tutorEffect });

        map.AddAbility(tutorAbility);

        return map;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="map"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by Mind
    /// Stone / Pyrite Spellbomb / Aether Spellbomb.
    /// </summary>
    private static void SacrificeSelf(Artifact map, Player owner)
    {
        if (map.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(map);
        owner.Zones.Graveyard.AddCard(map);
        map.SetZone(ZoneType.Graveyard);
    }
}
