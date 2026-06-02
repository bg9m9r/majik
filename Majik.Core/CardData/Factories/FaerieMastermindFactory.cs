using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faerie Mastermind (March of the Machine: The
/// Aftermath, {1}{U}). Creature — Faerie Rogue 2/1. Oracle text (verified
/// against Scryfall):
///   "Flash
///    Flying
///    Whenever an opponent draws their second card each turn, you draw a card.
///    {3}{U}: Each player draws a card."
///
/// The base shape (name, Creature, Faerie + Rogue subtypes, {1}{U}, 2/1) is
/// materialised from the embedded JSON definition
/// (<c>faerie-mastermind.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two keyword markers, the
/// opponent-second-draw trigger, and the {3}{U} draw-everyone activated
/// ability are layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't express keyword markers, draw-count triggers, or activated
/// abilities (same posture as <see cref="LedgerShredderFactory"/> for the
/// per-turn "second X each turn" counter and <see cref="SpectralSailorFactory"/>
/// for the Flash + Flying body plus a draw activated ability).
///
/// ## Implemented (v1)
/// - <b>Flash (CR 702.8)</b> + <b>Flying (CR 702.9)</b> — keyword markers via
///   <see cref="KeywordAbility"/>. Flash routes through
///   <see cref="Majik.Core.Rules.TimingRules.CanCastAtInstantSpeed"/>; Flying
///   block restrictions via <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>"Whenever an opponent draws their second card each turn, you draw a
///   card." (CR 603.2 / 121.1)</b> — a <see cref="TriggeredAbility"/> over
///   <see cref="CardDrawnEvent"/>. Per-opponent draw counts are held in a
///   closure private to this card instance; the predicate increments the
///   drawing player's count on every <see cref="CardDrawnEvent"/> whose player
///   is NOT the controller (an opponent — CR 102.1 / 109.5; the engine's
///   combat / turn model is two-player, so any non-controller drawer is an
///   opponent) and matches only on the exact transition to the second draw
///   (CR 603.3 — a trigger fires only when its condition becomes true; the
///   third+ draw does not retrigger). The controller's own draws never match,
///   honouring "an opponent draws". Counts reset on a
///   <see cref="TurnStartedEvent"/> (CR 500.1) when an event bus is supplied.
///   Effect = the controller draws one card (CR 121.1).
/// - <b>"{3}{U}: Each player draws a card." (CR 113.3b / 605 / 121.1)</b> — an
///   <see cref="ActivatedAbility"/> with a single {3}{U}
///   <see cref="ManaCostCost"/>. Not a mana ability (CR 605.1 — produces no
///   mana), so it routes through the normal stack. On resolution every player
///   the supplied <paramref name="allPlayersResolver"/> returns draws one card
///   in turn order (CR 101.4 / APNAP is moot for a single simultaneous draw —
///   the printed instruction draws one card for each player). Without a
///   resolver the effect is a structural no-op (shape / dispatcher path).
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect-aware draws</b>: both the trigger's "you draw a
///   card" and the activated ability's per-player draw move the top card
///   directly Library → Hand rather than routing through a unified
///   <c>DrawCardService</c>. Notion Thief / Dauthi Voidwalker-style draw
///   replacements (CR 121.8) therefore do not fire here. Same posture as
///   <see cref="SpectralSailorFactory"/>'s draw — to be revisited when the
///   draw replacement-bus rework lands.
/// - <b>Empty-library loss flag</b>: a direct zone-move no-ops on an empty
///   library; it does not set the "tried to draw from empty library" loss flag
///   (CR 104.3c / 704.5c). The SBA loop handles the loss elsewhere only for
///   the turn-based draw path. Same shortcut as Spectral Sailor.
/// </summary>
[CardName("Faerie Mastermind")]
public static class FaerieMastermindFactory
{
    public const string CardName = "Faerie Mastermind";
    public const string Slug = "faerie-mastermind";
    public const string ActivatedCost = "{3}{U}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Faerie Mastermind with no live runtime wiring (the
    /// dispatcher / shape path). Flash + Flying, the opponent-second-draw
    /// trigger, and the {3}{U} activated ability are attached for shape
    /// observability; the per-opponent count is never reset and the activated
    /// ability draws for nobody (no all-players resolver). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, allPlayersResolver: null);

    /// <summary>
    /// Construct Faerie Mastermind wired for the opponent-second-draw trigger
    /// (event bus + trigger manager) but without an all-players resolver — the
    /// {3}{U} activated ability then draws for nobody. Convenience overload for
    /// trigger tests.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers) =>
        Create(owner, eventBus, triggers, allPlayersResolver: null);

    /// <summary>
    /// Construct Faerie Mastermind with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus. When supplied, a
    /// <see cref="TurnStartedEvent"/> handler resets the per-opponent draw
    /// counts (CR 500.1). May be null.</param>
    /// <param name="triggers">TriggerManager the opponent-second-draw trigger
    /// registers with so a <see cref="CardDrawnEvent"/> lands it on the stack.
    /// May be null.</param>
    /// <param name="allPlayersResolver">Returns every player in the game at
    /// {3}{U} resolution; each draws one card. May be null — the activated
    /// ability then draws for nobody (shape path).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Faerie
        // + Rogue, {1}{U}, 2/1). No abilities in the JSON — all four printed
        // behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.8 — Flash. Allows casting at instant speed via TimingRules.
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        // CR 702.9 — Flying. Block restrictions enforced by CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        AddOpponentSecondDrawTrigger(card, owner, eventBus, triggers);
        AddEachPlayerDrawsAbility(card, owner, allPlayersResolver);

        return card;
    }

    // -----------------------------------------------------------------------
    // "Whenever an opponent draws their second card each turn, you draw a
    // card." (CR 603.2 / 603.3 / 121.1.)
    // -----------------------------------------------------------------------
    private static void AddOpponentSecondDrawTrigger(
        Creature card,
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        // Per-opponent draw count this turn, keyed by player. Closure shared
        // between the trigger predicate and the TurnStartedEvent reset handler.
        var drawsThisTurn = new Dictionary<Player, int>();

        var condition = new EventTriggerCondition<CardDrawnEvent>((e, _) =>
        {
            var drawer = e.Player;
            // "an opponent draws" — the controller's own draws never match
            // (CR 109.5 / 102.1). Two-player model: any non-controller drawer
            // is an opponent.
            if (ReferenceEquals(drawer, card.Controller ?? owner)) return false;

            drawsThisTurn.TryGetValue(drawer, out var count);
            count++;
            drawsThisTurn[drawer] = count;

            // CR 603.3 — fire only on the exact transition to the second draw;
            // the third+ draw this turn does not retrigger.
            return count == 2;
        });

        var drawEffect = new Effect(
            $"{CardName}: you draw a card (opponent drew their second card this turn)",
            () => DrawCard(card.Controller ?? owner));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { drawEffect },
            // CR 113.6 — functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // CR 500.1 — reset the per-opponent counts when a new turn starts.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => drawsThisTurn.Clear());
        }
    }

    // -----------------------------------------------------------------------
    // "{3}{U}: Each player draws a card." (CR 113.3b / 605 / 121.1.)
    // -----------------------------------------------------------------------
    private static void AddEachPlayerDrawsAbility(
        Creature card,
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        var drawAllEffect = new Effect(
            $"{CardName}: each player draws a card ({ActivatedCost} activated)",
            () =>
            {
                var players = allPlayersResolver?.Invoke();
                if (players == null) return; // shape path — no resolver wired.
                foreach (var player in players)
                {
                    if (player == null) continue;
                    DrawCard(player);
                }
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ActivatedCost) },
            effects: new IEffect[] { drawAllEffect });

        card.AddAbility(ability);
    }

    /// <summary>
    /// Direct Library → Hand zone-move (CR 121.1). No-op on empty library; see
    /// the class xmldoc for the draw-replacement-bus / empty-library-loss-flag
    /// gaps. Mirrors <see cref="SpectralSailorFactory"/>'s draw helper.
    /// </summary>
    private static void DrawCard(Player player)
    {
        var top = player.Zones.Library.GetCards().FirstOrDefault();
        if (top == null) return; // empty library — see class xmldoc.
        player.Zones.Library.RemoveCard(top);
        player.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
