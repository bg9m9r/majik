using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Knight of the White Orchid (Shards of Alara /
/// reprints, {W}{W}). Creature — Human Knight 2/2. Oracle text (verified
/// against Scryfall 2026-05):
///   "First strike
///    When this creature enters, if an opponent controls more lands than
///    you, you may search your library for a Plains card, put it onto the
///    battlefield, then shuffle."
///
/// The base shape (name, Creature, Human/Knight subtypes, {W}{W}, 2/2) is
/// materialised from the embedded JSON definition
/// (<c>knight-of-the-white-orchid.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// (First strike, the intervening-if ETB land tutor) are layered on here —
/// the JSON <c>AbilityDefinition</c> schema doesn't express an intervening-if
/// triggered tutor, so it lives in the factory (same posture as
/// <see cref="BladeSplicerFactory"/> and the other JSON-backed cards whose
/// behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Knight at {W}{W}, owner/controller wired (from JSON).
/// - <b>First strike (CR 702.7)</b> via a
///   <see cref="KeywordAbility"/> with keyword string "First strike" — the
///   exact token <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/>
///   reads (same marker as <see cref="PhyrexianCrusaderFactory"/> /
///   <see cref="BorosReckonerFactory"/>).
/// - <b>ETB triggered ability with an intervening-if (CR 603.4 / CR 603.6a)</b>
///   over <see cref="Triggers.OnEnterBattlefieldSelf"/>. The "an opponent
///   controls more lands than you" comparison reads the LIVE game off the
///   resolution context (<c>ctx.Game.AllPlayers</c>, filtered to
///   non-controller / not-lost) at resolution — NOT a captured resolver. The
///   predicate reads true iff at least one opponent controls strictly more
///   lands than the controller (CR 603.4 — "more lands than you" is a strict
///   comparison; a tie does not satisfy it). The authoritative check is at
///   resolution; the parameterless stack-placement intervening-if is left null
///   (it has no GameContext to read the live board), so a do-nothing instance
///   of the "may" ability may briefly sit on the stack when no opponent
///   out-lands you — its resolution is a clean no-op.
/// - <b>"May search ... for a Plains card, put it onto the battlefield, then
///   shuffle" (CR 701.19a / CR 701.20a)</b>. "A Plains card" reads the Plains
///   land subtype (CR 305.6) so it matches basic Plains and any non-basic
///   land typed Plains (e.g. a dual). The controller's agent
///   (<see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) is consulted; a null
///   return is a legal "may" decline. The pick moves Library → Battlefield
///   untapped (no "tapped" qualifier). The library is shuffled once after the
///   search via <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a).
///   Without a registered agent the deterministic first-Plains pick is taken
///   (mirrors <see cref="BurnishedHartFactory"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal events</b> — the tutored Plains moves Library → Battlefield
///   without publishing a reveal event (same gap as
///   <see cref="BurnishedHartFactory"/> / <see cref="SylvanScryingFactory"/>).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + First strike + ETB trigger
///   attached. The land-count comparison reads the live resolution context, so
///   it is correct on the production routed build (no captured resolver). This
///   is the overload <see cref="NamedCardFactory"/> / the routed prod build
///   dispatches to; the live engine registers the trigger via
///   <c>TriggerManager.BindCard</c> when the Knight enters.
/// - <see cref="Create(Player, TriggerManager?)"/> — additionally registers the
///   ETB trigger with the supplied <see cref="TriggerManager"/> for tests that
///   drive the bus-fired trigger path directly.
/// </summary>
[CardName("Knight of the White Orchid")]
public static class KnightOfTheWhiteOrchidFactory
{
    public const string CardName = "Knight of the White Orchid";
    public const string Slug = "knight-of-the-white-orchid";

    /// <summary>
    /// Shape-only overload — First strike + ETB trigger attached without
    /// registering with a <see cref="TriggerManager"/>. The intervening-if
    /// land-count comparison reads the live resolution context, so it is
    /// correct on the production routed build (see the class xmldoc). This is
    /// the overload <see cref="NamedCardFactory"/> / the routed prod build
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null);

    /// <summary>
    /// Construct Knight of the White Orchid with its ETB trigger attached and
    /// optionally registered against the supplied <paramref name="triggers"/>
    /// manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so a
    /// qualifying <see cref="Majik.Core.Events.CardMovedEvent"/> queues the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human/Knight subtypes, {W}{W}, 2/2). The JSON carries no abilities —
        // First strike + the ETB tutor are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.7 — First strike. The keyword string "First strike" is the
        // exact token CombatAbilities.HasFirstStrike reads.
        card.AddAbility(new KeywordAbility("First strike", source: card, controller: owner));

        // --------------------------------------------------------------------
        // ETB triggered ability with an intervening-if — CR 603.4 / 603.6a.
        //   "When this creature enters, if an opponent controls more lands
        //    than you, you may search your library for a Plains card, put it
        //    onto the battlefield, then shuffle."
        //
        // The "an opponent controls more lands than you" comparison reads the
        // LIVE game at RESOLUTION (ctx.Game.AllPlayers, filtered to
        // non-controller / not-lost) — NOT a captured opponentsResolver. The
        // production routed build (GameFacade.BuildDeckCard →
        // NamedCardFactory.Create(name, owner, effects) → single-arg shape
        // build) left that resolver null, so the predicate ALWAYS read false in
        // real games and the Knight NEVER fetched a Plains (only the resolver-
        // injecting factory-direct tests saw the tutor). Reading the live
        // context fixes the routed build (mirrors Stormbreath #2540 / Yawgmoth +
        // Priest #2543). "More lands than you" is strict — a tie does NOT
        // satisfy it.
        //
        // CR 603.4 — the intervening-if is normally checked both as the ability
        // would go on the stack (CanBePutOnStack) AND again as it resolves. The
        // stack-placement check is parameterless and has no GameContext to read
        // the live board from, so it is left permissive (interveningIf: null —
        // the trigger goes on the stack) and the AUTHORITATIVE check happens at
        // resolution, where ctx.Game is live. The only observable effect of the
        // deferred stack-placement check is that a do-nothing instance of the
        // "may" ability can sit on the stack when no opponent out-lands you;
        // its resolution is a clean no-op (no Plains is fetched), so the tutor
        // outcome is correct. This is the same resolution-time-gating posture
        // every other opponent-board-conditional ETB in the engine uses
        // (e.g. the OracleTriggeredAbilityBinder opponent riders).
        // --------------------------------------------------------------------
        bool AnOpponentControlsMoreLands(ResolutionContext ctx)
        {
            var players = ctx.Game?.AllPlayers;
            if (players == null) return false;
            var controller = card.Controller ?? owner;
            var myLands = CountLands(controller);
            foreach (var opp in players)
            {
                // CR 102.1 — the controller is never their own opponent.
                if (ReferenceEquals(opp, controller)) continue;
                if (opp.HasLost) continue;
                if (CountLands(opp) > myLands) return true;
            }
            return false;
        }

        var etbEffect = new Effect(
            $"{CardName}: if an opponent controls more lands, may tutor a Plains to battlefield, then shuffle",
            async ctx =>
            {
                // CR 603.4 — authoritative resolution-time check of the
                // intervening-if, reading the live game off the context.
                if (!AnOpponentControlsMoreLands(ctx)) return;
                var controller = card.Controller ?? owner;
                await TutorPlainsToBattlefieldAsync(controller, ctx).ConfigureAwait(false);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            // interveningIf left null — see the block comment above (CanBePutOnStack
            // has no GameContext; the authoritative check is at resolution).
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }

    /// <summary>
    /// CR 109.5 / CR 305 — count the land permanents
    /// <paramref name="player"/> controls (reads each card's current
    /// controller via the battlefield zone).
    /// </summary>
    public static int CountLands(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.Zones.Battlefield.GetCards().Count(c => c.HasType(CardType.Land));
    }

    /// <summary>
    /// CR 701.19a / CR 701.20a — "may search your library for a Plains card,
    /// put it onto the battlefield, then shuffle." Consults the agent
    /// (null = legal decline); deterministic first-Plains fallback when no
    /// agent is registered (mirrors <see cref="BurnishedHartFactory"/>). The
    /// pick moves Library → Battlefield untapped (no "tapped" qualifier).
    /// Shuffles once after the search even when nothing was found.
    ///
    /// "A Plains card" reads the Plains land subtype (CR 305.6) — matches
    /// basic Plains and any non-basic land typed Plains.
    /// </summary>
    private static async ValueTask TutorPlainsToBattlefieldAsync(Player player, ResolutionContext ctx)
    {
        bool IsPlainsCard(ICard c) => c.HasSubtype(CardSubtype.Plains);

        var candidates = player.Zones.Library.GetCards().Where(IsPlainsCard).ToList();

        ICard? pick = null;
        if (candidates.Count > 0)
        {
            var agent = ctx.Agent ?? AgentRegistry.Get(player);
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "Plains card to put onto the battlefield").ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                // Async move so the ResolutionContext (carrying the agent)
                // reaches a prompting ETB replacement — a Plains-typed shock
                // land (Hallowed Fountain, Sacred Foundry, …) must offer the
                // "pay 2 life?" choice (ShockLandReplacement.ReplaceAsync)
                // instead of auto-paying via the synchronous replacement
                // path. The land enters untapped, so the prompt is
                // load-bearing.
                await zones.MoveCardToAsync(pick, ZoneType.Battlefield, ctx, controller: player)
                    .ConfigureAwait(false);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                if (pick is Permanent perm) perm.SetController(player);
            }
        }

        // CR 701.20a — shuffle once after the search, even when nothing
        // was found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "knight-of-the-white-orchid");
    }
}
