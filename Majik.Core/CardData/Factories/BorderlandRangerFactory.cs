using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Borderland Ranger (Magic 2010 / reprints, {2}{G}).
///
/// Creature — Human Scout Ranger 2/2. Oracle text:
///   "When this creature enters, you may search your library for a basic land
///    card, reveal it, put it into your hand, then shuffle."
///
/// ## Shape source
/// Card identity (name, {2}{G}, 2/2, Creature — Human Scout Ranger) is loaded
/// from <c>Majik.Core/CardData/Cards/borderland-ranger.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The single ETB triggered ability is
/// attached in code below: the JSON ability schema does not yet express a
/// "search for a basic land → hand → shuffle" effect, so it is hand-rolled
/// here — same posture as the suggested analogue
/// <see cref="SolemnSimulacrumFactory"/> (which tutors to the battlefield) and
/// <see cref="BurnishedHartFactory"/> (the up-to-two-basics tutor).
///
/// ## Implemented (v1)
/// - 2/2 Human Scout Ranger (CR 205.3m) at {2}{G}.
/// - <b>ETB trigger (CR 603.6a)</b>: "you may search your library for a basic
///   land card, reveal it, put it into your hand, then shuffle." Searches for
///   ONE basic land (CR 305.6 — Basic supertype + Land card type), consults the
///   registered <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (CR 701.19a — the agent
///   may decline; "you may" + the search can fail to find, both legal). Moves
///   the pick Library → Hand and shuffles ONCE via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a — a single search
///   effect performs one shuffle, whether or not a card was found).
///   Deterministic first-basic fallback when no agent is registered — same
///   posture as <see cref="SolemnSimulacrumFactory"/>.
///
/// ## Deferred (v1)
/// - "You may" auto-accepts in v1 (the search consults the agent, which may
///   decline) — consistent with the rest of the factory family.
/// - <b>Reveal step</b>: the tutored basic moves Library → Hand without
///   publishing a reveal event — same gap as every tutor factory
///   (<see cref="SolemnSimulacrumFactory"/>, <see cref="BurnishedHartFactory"/>,
///   Cultivate). The card still reaches the hand, so the observable game state
///   is correct; only the public "reveal" UI signal is absent.
/// </summary>
[CardName("Borderland Ranger")]
public static class BorderlandRangerFactory
{
    public const string CardName = "Borderland Ranger";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("borderland-ranger");

    /// <summary>
    /// Construct Borderland Ranger with its ETB trigger attached to the card
    /// shape but NOT registered with a <see cref="TriggerManager"/>. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Borderland Ranger with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant <c>CardMovedEvent</c> places it on the stack
    /// automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may search your library for a
        //    basic land card, reveal it, put it into your hand, then
        //    shuffle."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: search a basic land -> hand, then shuffle",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return TutorOneBasicToHandAsync(controller, ctx);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent (which
    /// may decline; deterministic first-basic fallback when no agent), move the
    /// pick Library → Hand, then shuffle once (CR 701.20a). The printed "reveal
    /// it" step is a no-op signal in v1 (same gap as every tutor factory) — the
    /// card still reaches the hand so the observable game state is correct.
    /// </summary>
    private static async ValueTask TutorOneBasicToHandAsync(Player player, ResolutionContext ctx)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "basic land card to put into your hand")
                    .ConfigureAwait(false)
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
        LibraryShuffle.ShuffleLibrary(player, "borderland-ranger");
    }
}
