using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fauna Shaman (Magic 2011, {1}{G}).
///
/// Creature — Elf Shaman 2/2. Oracle text (Scryfall, verified):
///   "{G}, {T}, Discard a creature card: Search your library for a creature
///    card, reveal it, put it into your hand, then shuffle."
///
/// ## Shape source
/// Card identity (name, {1}{G}, 2/2, Creature — Elf Shaman) is loaded from
/// <c>Majik.Core/CardData/Cards/fauna-shaman.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The single activated ability is
/// attached in code below — the JSON ability schema does not yet express a
/// "search your library → hand → shuffle" effect, so it is hand-rolled here,
/// same posture as <see cref="BorderlandRangerFactory"/>.
///
/// ## Implemented (v1)
/// - 2/2 Elf Shaman (CR 205.3m) at {1}{G}.
/// - <b>Activated ability (CR 602.1)</b>:
///   <c>{G}, {T}, Discard a creature card: Search your library for a creature
///    card, reveal it, put it into your hand, then shuffle.</c>
///   The three activation costs are composed from existing primitives:
///   <see cref="ManaCostCost"/> ("{G}"), <see cref="AdditionalCost.Tap"/> on
///   the source (CR 602.1 / 107.4 — the {T} symbol), and a
///   <see cref="DiscardACreatureCardCost"/> (CR 117.1 / 701.16a — discard one
///   creature card from hand; the same creature-card-filtered cost Lotleth
///   Troll uses). The ability is repeatable while the controller can pay.
/// - <b>Resolution — tutor a creature card to hand</b>: searches the
///   controller's library for ONE creature card (CR 109.3 — "creature card" =
///   a card with the Creature type in any zone), consults the registered
///   <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (CR 701.19a — the
///   search may decline / fail to find, both legal), moves the pick
///   Library → Hand, then shuffles ONCE via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a — one shuffle per
///   search effect, whether or not a card was found). Deterministic
///   first-creature fallback when no agent is registered — same posture as
///   <see cref="BorderlandRangerFactory"/> / <see cref="StoneforgeMysticFactory"/>.
///
/// ## Deferred (v1)
/// - <b>Reveal step</b>: the tutored creature moves Library → Hand without
///   publishing a reveal event — same gap as every tutor factory
///   (<see cref="BorderlandRangerFactory"/>, <see cref="StoneforgeMysticFactory"/>).
///   The card still reaches the hand, so the observable game state is correct;
///   only the public "reveal" UI signal is absent.
/// </summary>
[CardName("Fauna Shaman")]
public static class FaunaShamanFactory
{
    public const string CardName = "Fauna Shaman";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("fauna-shaman");

    /// <summary>Activation mana cost — the {G} in "{G}, {T}, Discard …".</summary>
    public const string ActivationManaCost = "{G}";

    /// <summary>
    /// Construct Fauna Shaman. The activated ability is fully attached and
    /// exercisable. Movement falls back to <see cref="ZoneServiceRegistry"/>
    /// (when a service is registered for the controller) or raw zone moves.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "{G}, {T}, Discard a creature card: Search your library for a
        //  creature card, reveal it, put it into your hand, then shuffle."
        // CR 602.1 — activated ability. Three costs:
        //   {G}                  -> ManaCostCost
        //   {T}                  -> AdditionalCost.Tap (CR 107.4 / 602.1)
        //   Discard a creature   -> DiscardACreatureCardCost (CR 701.16a)
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: search your library for a creature card -> hand, then shuffle",
            () =>
            {
                var controller = card.Controller ?? owner;
                TutorOneCreatureToHand(controller);
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationManaCost),
                AdditionalCost.Tap(card),
                new DiscardACreatureCardCost(),
            },
            effects: new IEffect[] { tutorEffect });

        card.AddAbility(activated);

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE creature card
    /// (CR 109.3 — Creature card type in any zone), consult the agent (which
    /// may decline; deterministic first-creature fallback when no agent is
    /// registered), move the pick Library → Hand, then shuffle once
    /// (CR 701.20a). The printed "reveal it" step is a no-op signal in v1
    /// (same gap as every tutor factory) — the card still reaches the hand so
    /// the observable game state is correct.
    /// </summary>
    private static void TutorOneCreatureToHand(Player player)
    {
        bool IsCreatureCard(ICard c) => c.HasType(CardType.Creature);

        var agent = AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsCreatureCard).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, candidates,
                        "creature card to put into your hand")
                    .GetAwaiter().GetResult()
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Hand, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "fauna-shaman");
    }
}
