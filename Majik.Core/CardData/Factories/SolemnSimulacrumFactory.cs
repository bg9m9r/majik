using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Solemn Simulacrum (numerous reprints, {4}).
///
/// Artifact Creature — Golem 2/2. Oracle text:
///   "When this creature enters, you may search your library for a basic land
///    card, put that card onto the battlefield tapped, then shuffle.
///    When this creature dies, you may draw a card."
///
/// ## Shape source
/// Card identity (name, {4}, 2/2, Artifact + Creature — Golem) is loaded from
/// <c>Majik.Core/CardData/Cards/solemn-simulacrum.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The two triggered abilities are
/// attached in code below: the JSON ability schema does not yet express a
/// "search for A basic land → battlefield tapped → shuffle" effect nor a
/// "dies" trigger, so they are hand-rolled here — same posture as the
/// suggested analogue <see cref="BurnishedHartFactory"/> (tutor logic) and
/// <see cref="AvenFisherFactory"/> (dies → you may draw).
///
/// ## Implemented (v1)
/// - 2/2 Golem with BOTH Artifact and Creature card types (CR 205.2a) — the
///   JSON lists both types; <see cref="CardDefinitionFactory"/> adds the
///   secondary type so artifact-matters effects see it.
/// - <b>ETB trigger (CR 603.6a)</b>: "you may search your library for a basic
///   land card, put that card onto the battlefield tapped, then shuffle."
///   Searches for ONE basic land (CR 305.6 — Basic supertype + Land card
///   type), consults the registered <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> (CR 701.19a — agent may
///   decline; "you may" + the search can fail to find, both legal). Moves the
///   pick Library → Battlefield through <see cref="ZoneServiceRegistry"/> so
///   ETB-tapped replacements + <c>CardMovedEvent</c> subscribers fire, applies
///   the printed "tapped" rider after the move (CR 701.18), then shuffles ONCE
///   via <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a — a single
///   search effect performs one shuffle). Deterministic first-basic fallback
///   when no agent is registered — same posture as Burnished Hart.
/// - <b>Dies trigger (CR 603.6c / 700.4)</b>: "you may draw a card." Fires on
///   Battlefield → Graveyard; active in both zones because
///   <see cref="Majik.Core.Zones.ZoneService"/> stamps <c>card.Zone =
///   Graveyard</c> before publishing the <c>CardMovedEvent</c> (mirrors Aven
///   Fisher / Stitcher's Supplier). Draw routed through
///   <see cref="Fx.DrawCards"/> so draw-replacement + empty-library SBA loss
///   fire per CR 121.1 / CR 704.5c.
///
/// ## Deferred (v1)
/// - "You may" decisions auto-accept in v1 (search consults the agent; the
///   draw is unconditional) — consistent with the rest of the factory family.
/// - Tutored basic moves Library → Battlefield without a reveal event — same
///   gap as every tutor factory.
/// </summary>
[CardName("Solemn Simulacrum")]
public static class SolemnSimulacrumFactory
{
    public const string CardName = "Solemn Simulacrum";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("solemn-simulacrum");

    /// <summary>
    /// Construct Solemn Simulacrum with both triggers attached to the card
    /// shape but NOT registered with a <see cref="TriggerManager"/>. Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Solemn Simulacrum with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, both triggers are
    /// registered so the relevant <c>CardMovedEvent</c> places them on the
    /// stack automatically (CR 603.3).
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
        //    basic land card, put that card onto the battlefield tapped,
        //    then shuffle."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: search a basic land -> battlefield tapped, then shuffle",
            () =>
            {
                var controller = card.Controller ?? owner;
                TutorOneBasicToBattlefieldTapped(controller);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Dies triggered ability — CR 603.6c / 700.4.
        //   "When this creature dies, you may draw a card."
        // Active in Battlefield + Graveyard: ZoneService stamps the zone
        // before publishing the CardMovedEvent (mirrors Aven Fisher /
        // Stitcher's Supplier). "You may" auto-accepts in v1.
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName} dies: draw a card (you may — v1 auto-accepts)",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, 1); // CR 121.1
            });

        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { diesEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent (which
    /// may decline; deterministic first-basic fallback when no agent), move
    /// the pick to the battlefield with the printed "tapped" rider applied
    /// after the move (CR 701.18), then shuffle once (CR 701.20a).
    /// </summary>
    private static void TutorOneBasicToBattlefieldTapped(Player player)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            pick = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, candidates,
                        "basic land card to put onto the battlefield tapped")
                    .GetAwaiter().GetResult()
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
                if (pick is Permanent perm && !perm.IsTapped) perm.Tap();
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(player);
                if (pick is Permanent perm) perm.Tap();
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards
        // were found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "solemn-simulacrum");
    }
}
