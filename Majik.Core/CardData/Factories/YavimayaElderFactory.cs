using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yavimaya Elder (Weatherlight / reprints, {1}{G}{G}).
///
/// Creature — Human Druid 2/1. Oracle text (verified against Scryfall):
///   "When this creature dies, you may search your library for up to two basic
///    land cards, reveal them, put them into your hand, then shuffle.
///    {2}, Sacrifice this creature: Draw a card."
///
/// The card's base shape (name, Creature, Human/Druid subtypes, {1}{G}{G},
/// 2/1) is materialised from the embedded JSON definition
/// (<c>yavimaya-elder.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (dies trigger + sac-draw activated ability) are layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express triggered or activated
/// abilities, so they live in the factory (same posture as the other
/// JSON-backed cards whose behaviour outgrows the schema, e.g.
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - 2/1 Creature — Human Druid, mana cost {1}{G}{G}.
/// - <b>Dies trigger</b> (CR 603.6c / 700.4): "When this creature dies, you
///   may search your library for up to two basic land cards, reveal them, put
///   them into your hand, then shuffle." Wired as a
///   <see cref="TriggeredAbility"/> over <see cref="Triggers.OnDies"/> with
///   active zones {Battlefield, Graveyard} — ZoneService stamps
///   card.Zone = Graveyard before publishing the move event, so the trigger
///   must remain observable in the graveyard (same posture as Doomed
///   Traveler / Aven Fisher / Wurmcoil Engine). On resolve it tutors up to
///   two basic land cards (CR 305.6 — Basic supertype + Land card type) to
///   the controller's HAND, consulting the agent twice (each pick may decline;
///   "up to two" permits 0..2; "you may" makes the whole search optional, so
///   a null/declining agent is fully legal — deterministic first-two-basics
///   fallback when no agent), then shuffles ONCE (CR 701.20a — one shuffle per
///   search effect even with multiple cards found). Mirrors
///   <see cref="BurnishedHartFactory"/>'s up-to-two-basics tutor, retargeted
///   from battlefield-tapped to hand.
/// - <b>{2}, Sacrifice this creature: Draw a card</b> — an
///   <see cref="ActivatedAbility"/> with <see cref="ManaCostCost"/>("{2}") +
///   <see cref="AdditionalCost.Sacrifice"/> on the elder itself (NO {T} pip —
///   the printed line is just "{2}, Sacrifice this creature:"). Resolution
///   moves the elder to its owner's graveyard and draws one card for the
///   controller via <see cref="Fx.DrawCards"/>. Same sac-draw shape as Pyrite
///   Spellbomb's {R} cantrip mode. Sacrificing the elder this way naturally
///   triggers its own dies ability (CR 603.6c) when registered with a
///   TriggerManager.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the tutored basics move Library → Hand without
///   publishing a reveal event. Same gap as every tutor factory.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub; the effect closure performs the zone move directly so behaviour is
///   observable — same posture as Burnished Hart / Pyrite Spellbomb.
///
/// ## Rules reference
/// - CR 603.6c — when a triggered ability's condition is met, it triggers.
/// - CR 700.4 — "dies" means moved from the battlefield to the graveyard.
/// - CR 305.6 — basic land = Basic supertype + Land card type.
/// - CR 701.20a — one shuffle per search effect.
/// - CR 602 — activated ability with mana + sacrifice costs.
/// </summary>
[CardName("Yavimaya Elder")]
public static class YavimayaElderFactory
{
    public const string CardName = "Yavimaya Elder";
    public const string Slug = "yavimaya-elder";

    /// <summary>
    /// Construct Yavimaya Elder with both abilities attached to the card shape
    /// but the dies trigger NOT registered with a <see cref="TriggerManager"/>.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Yavimaya Elder with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the dies trigger
    /// is registered so a Battlefield → Graveyard move places it on the stack
    /// automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Druid subtypes, {1}{G}{G}, 2/1). The JSON carries no
        // abilities — the dies trigger + sac-draw are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // When this creature dies, you may search your library for up to two
        // basic land cards, reveal them, put them into your hand, then
        // shuffle. CR 603.6c / 700.4 — "dies" = Battlefield → Graveyard.
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName} dies: search up to two basic lands -> hand, then shuffle",
            () =>
            {
                var controller = card.Controller ?? owner;
                TutorUpToTwoBasicsToHand(controller);
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            // Battlefield + Graveyard: ZoneService stamps zone BEFORE the
            // CardMovedEvent fires, so the trigger must be active in both
            // zones to evaluate correctly (mirrors Doomed Traveler / Aven
            // Fisher / Wurmcoil Engine pattern).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // ----------------------------------------------------------------
        // {2}, Sacrifice this creature: Draw a card.
        // CR 602 — activated ability with mana + sac costs, NO {T} pip.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: sac self + draw a card",
            () =>
            {
                SacrificeSelf(card, owner);
                Fx.DrawCards(owner, 1);
            });

        var drawAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { drawEffect });

        card.AddAbility(drawAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        var controller = card.Controller ?? owner;
        controller.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for up to two basic land
    /// cards (CR 305.6 — Basic supertype + Land card type), consult the agent
    /// twice (each pick may decline, "up to two" permits 0..2 picks; the "you
    /// may" clause makes the whole search optional so a null/declining agent
    /// is legal — deterministic first-two-basics fallback when no agent), move
    /// each pick to the hand, then shuffle once (CR 701.20a — one shuffle per
    /// search effect even when multiple cards are found). Mirrors
    /// <see cref="BurnishedHartFactory"/>, retargeted to the hand.
    /// </summary>
    private static void TutorUpToTwoBasicsToHand(Player player)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = AgentRegistry.Get(player);
        var picks = new List<ICard>(capacity: 2);

        // First pick.
        var firstCandidates = player.Zones.Library.GetCards()
            .Where(IsBasicLand).ToList();
        if (firstCandidates.Count > 0)
        {
            ICard? first = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, firstCandidates,
                        "basic land card to put into your hand")
                    .GetAwaiter().GetResult()
                : firstCandidates[0];
            if (first != null) picks.Add(first);
        }

        // Second pick (excluding the first).
        var secondCandidates = player.Zones.Library.GetCards()
            .Where(c => IsBasicLand(c) && (picks.Count == 0 || !ReferenceEquals(c, picks[0])))
            .ToList();
        if (secondCandidates.Count > 0)
        {
            ICard? second = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, secondCandidates,
                        "basic land card to put into your hand")
                    .GetAwaiter().GetResult()
                : secondCandidates[0];
            if (second != null) picks.Add(second);
        }

        var zones = ZoneServiceRegistry.Get(player);
        foreach (var pick in picks)
        {
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
        LibraryShuffle.ShuffleLibrary(player, "yavimaya-elder");
    }
}
