using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Horizon Spellbomb (Mirrodin / reprints).
///
/// Artifact — {1}. Oracle text (Scryfall, verified):
///   "{2}, {T}, Sacrifice this artifact: Search your library for a basic land
///    card, reveal it, put it into your hand, then shuffle.
///    When this artifact is put into a graveyard from the battlefield, you may
///    pay {G}. If you do, draw a card."
///
/// ## Shape source
/// Card identity (name, {1}, Artifact) is loaded from
/// <c>Majik.Core/CardData/Cards/horizon-spellbomb.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The activated ability and the dies
/// trigger are wired in code below.
///
/// ## Implemented (v1)
/// - <b>{2}, {T}, Sacrifice: search your library for a basic land card, reveal
///   it, put it into your hand, then shuffle</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>("{2}")
///   plus <see cref="AdditionalCost"/>.Tap and <see cref="AdditionalCost"/>.Sacrifice
///   on the spellbomb itself. The resolution effect searches for ONE basic land
///   (CR 305.6 — Basic supertype + Land card type), consults the registered
///   <see cref="IPlayerAgent"/> via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>
///   (CR 701.19a — the agent may decline; the search can fail to find, both
///   legal), moves the pick Library → Hand, then shuffles ONCE
///   (CR 701.20a — a single search effect performs one shuffle whether or not a
///   card was found). The tutor closure mirrors
///   <see cref="BorderlandRangerFactory"/>. The sacrifice is carried out by the
///   effect closure (mirrors <see cref="AetherSpellbombFactory"/> — the generic
///   <see cref="AdditionalCost.Pay"/> sacrifice path is a stub).
/// - <b>Dies trigger — CR 603.6c</b>: "When this artifact is put into a
///   graveyard from the battlefield, you may pay {G}. If you do, draw a card."
///   Fires on a Battlefield → Graveyard <see cref="CardMovedEvent"/> matching
///   this specific card. v1 auto-pays {G} when the controller's mana pool can
///   cover it ("you may" defaults to accepting when mana is available — same
///   posture as <see cref="NihilSpellbombFactory"/>); draws one card on success.
///   activeZones includes both Battlefield and Graveyard so the trigger is still
///   evaluated after ZoneService stamps Zone = Graveyard before publishing
///   (mirrors Wurmcoil Engine / Undying pattern).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal step</b>: the tutored basic moves Library → Hand without
///   publishing a reveal event — same gap as every tutor factory
///   (<see cref="BorderlandRangerFactory"/>, Cultivate). The card still reaches
///   the hand, so the observable game state is correct; only the public
///   "reveal" UI signal is absent.
/// - <b>"You may" prompt for {G} payment</b>: v1 auto-accepts payment when the
///   mana pool has {G} (same posture as <see cref="NihilSpellbombFactory"/>).
///   Real prompt deferred until the dies-trigger path threads an agent
///   yes/no surface.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op stub.
///   The effect closure performs the zone move so behavior is observable —
///   same posture as <see cref="AetherSpellbombFactory"/>.
/// </summary>
[CardName("Horizon Spellbomb")]
public static class HorizonSpellbombFactory
{
    public const string CardName = "Horizon Spellbomb";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("horizon-spellbomb");

    /// <summary>
    /// Construct Horizon Spellbomb. The dies trigger is attached to the card
    /// shape but not registered with a <see cref="TriggerManager"/> (suitable
    /// for shape and dispatcher tests).
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — see <see cref="FestivalCrasherFactory"/>). Threads
    /// <c>effects.EventBus</c> into the self-sacrifice cost so paying it
    /// publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a)
    /// crediting the cost-payer. The dies trigger auto-binds on the live
    /// manager's first zone crossing, so no TriggerManager is needed here.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, triggers: null, eventBus: effects?.EventBus);

    /// <summary>
    /// Construct Horizon Spellbomb with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the dies trigger is
    /// registered so a Battlefield → Graveyard <c>CardMovedEvent</c> places it on
    /// the stack automatically.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers) =>
        Create(owner, triggers, eventBus: null);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is threaded
    /// into the self-sacrifice <see cref="AdditionalCost"/> so the cost-payment
    /// path publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
    /// Null preserves the legacy publish-nothing posture.
    /// </summary>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var spellbomb = (Artifact)CardDefinitionFactory.Build(Definition, owner);
        spellbomb.SetOwner(owner);
        spellbomb.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}, Sacrifice this artifact: Search your library for a basic
        // land card, reveal it, put it into your hand, then shuffle.
        //
        // CR 602 — activated ability; not a mana ability (it has a tutor
        // effect, goes on the stack). Cost: {2} + tap + self-sacrifice
        // (Battlefield → Graveyard). No targets — it's a search of your own
        // library. The sacrifice is performed by the effect closure (the
        // generic AdditionalCost.Pay sacrifice path is a stub — same as
        // Aether Spellbomb).
        // ----------------------------------------------------------------
        var tutorEffect = new Effect(
            $"{CardName}: search a basic land -> hand, then shuffle + sac self",
            ctx =>
            {
                var controller = spellbomb.Controller ?? owner;
                SacrificeSelf(spellbomb, owner);
                return TutorOneBasicToHandAsync(controller, ctx);
            });

        var tutorAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(spellbomb),
                AdditionalCost.Sacrifice(spellbomb, eventBus),
            },
            effects: new IEffect[] { tutorEffect });

        spellbomb.AddAbility(tutorAbility);

        // ----------------------------------------------------------------
        // Dies trigger — CR 603.6c.
        //   "When this artifact is put into a graveyard from the
        //    battlefield, you may pay {G}. If you do, draw a card."
        //
        // Fires on a Battlefield → Graveyard CardMovedEvent matching this
        // specific card. v1 auto-pays {G} when the controller's mana pool can
        // cover it; draws one card on success. activeZones includes both
        // Battlefield and Graveyard so the trigger is still evaluated after
        // ZoneService stamps Zone = Graveyard before publishing (mirrors
        // Nihil Spellbomb / Wurmcoil Engine / Undying pattern).
        // ----------------------------------------------------------------
        var diesEffect = new Effect(
            $"{CardName}: may pay {{G}} to draw a card",
            () =>
            {
                // "You may pay {G}. If you do, draw a card."
                // v1 auto-accepts when the pool has the mana (same posture
                // as Nihil Spellbomb).
                var cost = ManaCost.Parse("{G}");
                if (!owner.ManaPool.CanPay(cost)) return;

                owner.PayMana(cost);

                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var diesTrigger = new TriggeredAbility(
            source: spellbomb,
            controller: owner,
            condition: Triggers.OnDies(spellbomb),
            effects: new IEffect[] { diesEffect },
            // activeZones: Battlefield + Graveyard so the trigger still
            // matches after ZoneService stamps Zone = Graveyard before
            // publishing (mirrors Nihil Spellbomb / Wurmcoil Engine).
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        spellbomb.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return spellbomb;
    }

    /// <summary>
    /// Search <paramref name="player"/>'s library for ONE basic land card
    /// (CR 305.6 — Basic supertype + Land card type), consult the agent (which
    /// may decline; deterministic first-basic fallback when no agent), move the
    /// pick Library → Hand, then shuffle once (CR 701.20a). The printed "reveal
    /// it" step is a no-op signal in v1 (same gap as every tutor factory) — the
    /// card still reaches the hand so the observable game state is correct.
    /// Mirrors <see cref="BorderlandRangerFactory"/>.
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
        LibraryShuffle.ShuffleLibrary(player, "horizon-spellbomb");
    }

    /// <summary>
    /// Move the spellbomb from the battlefield to its owner's graveyard.
    /// Idempotent — no-op if the card is already off the battlefield. Mirrors
    /// <see cref="AetherSpellbombFactory"/> — the generic
    /// <see cref="AdditionalCost.Pay"/> sacrifice path is a stub.
    /// </summary>
    private static void SacrificeSelf(Artifact spellbomb, Player owner)
    {
        if (spellbomb.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(spellbomb);
        owner.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);
    }
}
