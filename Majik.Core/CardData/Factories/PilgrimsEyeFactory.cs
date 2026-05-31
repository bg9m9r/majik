using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pilgrim's Eye (Conflux / many reprints, {3}).
/// Artifact Creature — Thopter 1/1. Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, you may search your library for a basic land
///    card, reveal it, put it into your hand, then shuffle."
///
/// The card's base shape (name, Creature + Artifact card types, Thopter
/// subtype, {3}, 1/1) is materialised from the embedded JSON definition
/// (<c>pilgrims-eye.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="SolemnSimulacrumFactory"/> (the other Artifact-Creature whose
/// JSON carries both card types). Flying + the ETB search-to-hand are layered
/// on here because the JSON schema doesn't express keywords or search effects.
///
/// ## Implemented (v1)
/// - 1/1 Creature — Thopter at {3} with both Artifact AND Creature card types
///   (CR 205.2a), via the JSON <c>["Creature", "Artifact"]</c> type list so
///   artifact-matters effects (Affinity, metalcraft) see it.
/// - <b>Flying (CR 702.9)</b> attached as a <see cref="KeywordAbility"/>
///   marker — the combat-block validator reads the keyword off the card's
///   abilities (same shape as <see cref="EldraziSkyspawnerFactory"/>).
/// - <b>ETB triggered ability (CR 603.6a)</b> over
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. Resolution searches the
///   controller's library for ONE basic land card (CR 305.6 — Basic supertype
///   + Land card type), consults the agent (which may decline — the printed
///   "you may" is honoured by a null pick, same posture as
///   <see cref="ExpeditionMapFactory"/> / <see cref="SolemnSimulacrumFactory"/>),
///   moves the pick Library → Hand (CR 701.19a), then shuffles once
///   (CR 701.20a) whether or not a card was found.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal-event emission</b>: the tutored basic moves Library → Hand
///   without publishing a reveal event. Same gap as every tutor-to-hand
///   factory (Expedition Map, etc.).
/// </summary>
[CardName("Pilgrim's Eye")]
public static class PilgrimsEyeFactory
{
    public const string CardName = "Pilgrim's Eye";
    public const string Slug = "pilgrims-eye";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Pilgrim's Eye with no live wiring. The ETB trigger is
    /// attached for shape observability (not registered with any
    /// <see cref="TriggerManager"/>). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Pilgrim's Eye with an optional <see cref="TriggerManager"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers with
    /// the bus so the corresponding enter-the-battlefield event lands the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact, Thopter subtype, {3}, 1/1). Flying + the ETB search are
        // layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. Keyword marker only; the combat-block validator
        // reads the keyword off the card's abilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may search your library for a
        //    basic land card, reveal it, put it into your hand, then shuffle."
        // No targets — pure search-to-hand.
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
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent
    /// (which may decline — "you may" is honoured by a null pick;
    /// deterministic first-basic fallback when no agent), move the pick
    /// Library → Hand (CR 701.19a), then shuffle once (CR 701.20a) whether
    /// or not a card was found. Mirrors
    /// <see cref="SolemnSimulacrumFactory"/>'s tutor closure (to-battlefield)
    /// and <see cref="ExpeditionMapFactory"/>'s to-hand land tutor.
    /// </summary>
    private static async ValueTask TutorOneBasicToHandAsync(Player player, ResolutionContext ctx)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();

        // CR 701.19a — prompt even on zero candidates so the human searcher
        // sees the failed search; the agent may decline (null) to honour the
        // printed "you may".
        var pick = await LibrarySearch.PromptOnlyAsync(
            ctx, player, candidates, "basic land card to put into your hand")
            .ConfigureAwait(false);

        if (pick != null)
        {
            player.Zones.Library.RemoveCard(pick);
            player.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "pilgrims-eye");
    }
}
