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
///   over <see cref="Triggers.OnEnterBattlefieldSelf"/>. The
///   <see cref="TriggeredAbility.InterveningIf"/> is checked twice (CR 603.4):
///   once as the ability would go on the stack and again as it resolves
///   (this factory's effect re-checks the predicate on resolution too). The
///   predicate reads true iff at least one opponent controls strictly more
///   lands than the controller (CR 603.4 — "more lands than you" is a strict
///   comparison; a tie does not satisfy it).
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
///   attached for inspection; the intervening-if reduces to false (no
///   opponents resolver, so no opponent can out-land you). This is the
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, Func{IReadOnlyList{Player}}?)"/>
///   — registers the ETB trigger with the supplied
///   <see cref="TriggerManager"/> and walks the supplied
///   <paramref name="opponentsResolver"/> for the land-count comparison.
/// </summary>
[CardName("Knight of the White Orchid")]
public static class KnightOfTheWhiteOrchidFactory
{
    public const string CardName = "Knight of the White Orchid";
    public const string Slug = "knight-of-the-white-orchid";

    /// <summary>
    /// Shape-only overload — First strike + ETB trigger attached without
    /// registering with a <see cref="TriggerManager"/>. The intervening-if
    /// reduces to false (no opponents resolver). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, opponentsResolver: null);

    /// <summary>
    /// Construct Knight of the White Orchid with its ETB trigger attached and
    /// optionally registered against the supplied <paramref name="triggers"/>
    /// manager.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB trigger registers so a
    /// qualifying <see cref="Majik.Core.Events.CardMovedEvent"/> queues the
    /// ability on the stack automatically (CR 603.2).</param>
    /// <param name="opponentsResolver">When supplied, the intervening-if
    /// (CR 603.4) walks this resolver's players and reads true iff one of
    /// them controls strictly more lands than the controller. Null →
    /// false (no opponent to out-land you — shape path).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? opponentsResolver)
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
        // CR 603.4 — the intervening-if is checked both when the ability would
        // be put on the stack (TriggeredAbility.CanBePutOnStack) AND again as
        // it resolves; this factory re-checks the predicate inside the effect
        // for the resolution check too. "More lands than you" is strict — a
        // tie does NOT satisfy it.
        // --------------------------------------------------------------------
        bool AnOpponentControlsMoreLands()
        {
            var opponents = opponentsResolver?.Invoke();
            if (opponents == null) return false;
            var controller = card.Controller ?? owner;
            var myLands = CountLands(controller);
            foreach (var opp in opponents)
            {
                if (opp == null || ReferenceEquals(opp, controller)) continue;
                if (CountLands(opp) > myLands) return true;
            }
            return false;
        }

        var etbEffect = new Effect(
            $"{CardName}: if an opponent controls more lands, may tutor a Plains to battlefield, then shuffle",
            () =>
            {
                // CR 603.4 — resolution-time re-check of the intervening-if.
                if (!AnOpponentControlsMoreLands()) return;
                var controller = card.Controller ?? owner;
                TutorPlainsToBattlefield(controller);
            });

        var etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: AnOpponentControlsMoreLands,
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
    private static void TutorPlainsToBattlefield(Player player)
    {
        bool IsPlainsCard(ICard c) => c.HasSubtype(CardSubtype.Plains);

        var candidates = player.Zones.Library.GetCards().Where(IsPlainsCard).ToList();

        ICard? pick = null;
        if (candidates.Count > 0)
        {
            var agent = AgentRegistry.Get(player);
            pick = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, candidates,
                        "Plains card to put onto the battlefield").GetAwaiter().GetResult()
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, player);
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
