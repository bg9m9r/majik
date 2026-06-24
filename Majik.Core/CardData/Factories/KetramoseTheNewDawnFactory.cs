using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ketramose, the New Dawn (Aetherdrift, {1}{W}{B}).
///
/// Legendary Creature — God 4/4. Oracle text (verified against Scryfall):
///   "Menace, lifelink, indestructible
///    Ketramose can't attack or block unless there are seven or more cards
///    in exile.
///    Whenever one or more cards are put into exile from graveyards and/or
///    the battlefield during your turn, you draw a card and lose 1 life."
///
/// The card's base shape (name, Legendary supertype, God subtype, {1}{W}{B},
/// 4/4) is materialised from the embedded JSON definition
/// (<c>ketramose-the-new-dawn.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed behaviours
/// (Menace / Lifelink / Indestructible keywords, the can't-attack-or-block
/// static, the exile-trigger draw-and-lose-life) are layered on here — the
/// JSON <c>AbilityDefinition</c> schema doesn't express keyword markers,
/// predicate-mode combat restrictions, or event-driven triggers, so they
/// live in the factory (same posture as <see cref="HazoretTheFerventFactory"/>
/// — the other indestructible God with a "can't attack or block unless
/// &lt;count&gt;" gate).
///
/// ## Implemented (v1)
///
/// - 4/4 Legendary Creature — God at {1}{W}{B}, owner / controller wired.
/// - <b>Menace (CR 702.111) + Lifelink (CR 702.15) + Indestructible
///   (CR 702.12)</b>: <see cref="KeywordAbility"/> markers. SBA 704.5g +
///   the destroy pipeline read Indestructible via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>;
///   Menace is read by the block-declaration validator, Lifelink by the
///   damage pipeline. Same wiring as <see cref="HazoretTheFerventFactory"/>
///   (Indestructible) + the keyword-marker idiom across the factory pool.
/// - <b>"Ketramose can't attack or block unless there are seven or more
///   cards in exile" (CR 508.1c / CR 509.1c)</b>: two predicate-mode
///   <see cref="CombatRestrictionEffect"/> instances
///   (<see cref="CombatRestriction.CannotAttack"/> +
///   <see cref="CombatRestriction.CannotBlock"/>), each self-scoped (the
///   predicate matches only when the queried creature IS Ketramose) and
///   tripping while fewer than seven cards sit in exile ("unless seven or
///   more" == "while fewer than seven"). The exile count is read live every
///   validation pass via <paramref name="exileCardCount"/>, so the lock
///   lifts the instant a seventh card hits exile. Gated on Ketramose being
///   on the battlefield (CR 603.6e). Same predicate-mode + self-scoped +
///   dual-restriction shape as <see cref="HazoretTheFerventFactory"/>'s
///   hand-size gate, but counting exile instead. Registered only when a
///   <see cref="ContinuousEffectsService"/> is supplied.
/// - <b>"Whenever one or more cards are put into exile from graveyards
///   and/or the battlefield during your turn, you draw a card and lose 1
///   life" (CR 603.1 / CR 603.2 / CR 603.3e)</b>: a
///   <see cref="TriggeredAbility"/> listening to <see cref="CardMovedEvent"/>
///   filtered to <c>ToZone == Exile</c> with
///   <c>FromZone ∈ {Graveyard, Battlefield}</c>, gated on it being the
///   controller's turn (<paramref name="isControllersTurn"/>). On resolution
///   the controller draws a card (<see cref="Fx.DrawCards"/>) then loses 1
///   life (<see cref="Fx.LoseLife"/>). Same event-driven exile trigger shape
///   as <see cref="SoulherderFactory"/>'s "creature exiled from the
///   battlefield" counter trigger.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape + keyword markers + the exile
///   trigger (not registered with a manager; gate defaults to controller's
///   turn = true). The combat restriction is NOT registered (no
///   continuous-effects service). The overload <see cref="NamedCardFactory"/>
///   dispatches to.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — additionally
///   registers the can't-attack-or-block restriction (counts the controller's
///   own exile zone for the count gate).
/// - <see cref="Create(Player, ContinuousEffectsService?, TriggerManager?,
///   Func{int}?, Func{bool}?)"/> — fully wired: restriction with a live
///   exile-count resolver, the trigger registered on a
///   <see cref="TriggerManager"/>, and the "during your turn" gate.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"One or more cards … batch" semantics (CR 603.3e)</b>: the engine's
///   <see cref="CardMovedEvent"/> fires per-card, so a single resolution that
///   exiles N cards simultaneously raises the trigger N times here rather
///   than once for the batch. v1 over-draws / over-pays life in that rare
///   case. Acceptable until a batched zone-change event lands (same posture
///   as the rest of the "one or more" trigger pool, e.g. Bridge from Below /
///   Bloodthirsty Conqueror).
/// - <b>Global exile count</b>: the restriction's count gate reads the
///   controller's own exile zone via the supplied resolver. The live game
///   wires <paramref name="exileCardCount"/> to "all cards in every player's
///   exile zone" (the shared exile zone, CR 406.2); the no-resolver two-arg
///   overload falls back to the controller's exile zone — defensive for
///   shape tests, identical posture to the resolver deferrals across the
///   pool (Hazoret's opponents resolver, Kaito's tap-target resolver).
/// - <b>Bot attack/block planner</b>: the heuristic bot does not yet read
///   the <see cref="CombatRestriction"/> when proposing attackers /
///   blockers; the engine rejects any illegal declaration the predicate
///   catches (same posture as Ensnaring Bridge / Hazoret).
///
/// CR rule references: 205.2 (Legendary), 205.3m (God subtype), 702.111
/// (Menace), 702.15 (Lifelink), 702.12 (Indestructible), 508.1c / 509.1c
/// (combat restrictions), 603.1 / 603.2 / 603.3e (triggered abilities),
/// 406.2 (exile is a single shared zone).
/// </summary>
[CardName("Ketramose, the New Dawn")]
public static class KetramoseTheNewDawnFactory
{
    public const string CardName = "Ketramose, the New Dawn";
    public const string Slug = "ketramose-the-new-dawn";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// CR 508.1c — "unless there are seven or more cards in exile" means the
    /// restriction is active while exile holds FEWER than this many cards.
    /// "Seven or more" lifts the lock; six or fewer re-imposes it.
    /// </summary>
    public const int ExileUnlockThreshold = 7;

    public const int DrawAmount = 1;
    public const int LifeLossAmount = 1;

    /// <summary>
    /// Construct Ketramose with no continuous-effects service and no
    /// runtime resolvers. Keyword markers + the exile trigger are attached
    /// (the trigger's "your turn" gate defaults to true so shape tests see
    /// it fire); the can't-attack-or-block restriction is NOT registered.
    /// Suitable for shape / dispatcher tests. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct Ketramose with a continuous-effects service. The combat
    /// restriction is registered; its count gate reads the controller's own
    /// exile zone (no global resolver supplied). The trigger is attached but
    /// not registered with a manager.
    /// </summary>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects)
        => Create(owner, continuousEffects, triggers: null, exileCardCount: null, isControllersTurn: null);

    /// <summary>
    /// Fully-wired Ketramose, the New Dawn.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Game-level continuous-effects service.
    /// When supplied, the two predicate-mode combat restrictions
    /// (CannotAttack + CannotBlock) are registered, gated on Ketramose being
    /// on the battlefield. Pass null to skip the restriction.</param>
    /// <param name="triggers">When supplied, the exile trigger is registered
    /// so bus <see cref="CardMovedEvent"/>s automatically queue it.</param>
    /// <param name="exileCardCount">Live count of cards in exile (the shared
    /// exile zone — CR 406.2) used by the can't-attack-or-block gate. Null =
    /// fall back to the controller's own exile zone.</param>
    /// <param name="isControllersTurn">Gate the exile trigger consults — true
    /// while it is Ketramose's controller's turn ("during your turn"). Null =
    /// always true (defensive for shape tests).</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers,
        Func<int>? exileCardCount,
        Func<bool>? isControllersTurn)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // supertype, God subtype, {1}{W}{B}, 4/4). The JSON carries no
        // abilities — the printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.111 / 702.15 / 702.12 — Menace + Lifelink + Indestructible
        // keyword markers.
        card.AddAbility(new KeywordAbility("Menace", card, owner));
        card.AddAbility(new KeywordAbility("Lifelink", card, owner));
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        // ----------------------------------------------------------------
        // "Ketramose can't attack or block unless there are seven or more
        // cards in exile." CR 508.1c (attack) + CR 509.1c (block).
        //
        // Predicate-mode CombatRestrictionEffect, self-scoped: the predicate
        // matches only when the queried creature IS Ketramose, and only while
        // exile holds FEWER than seven cards ("unless seven or more" ==
        // "while < 7"). The exile count is read live every validation pass,
        // so the lock lifts immediately the moment a seventh card hits exile.
        //
        // Count source: the supplied resolver (the live game wires it to the
        // shared exile zone across all players, CR 406.2); absent a resolver,
        // fall back to the controller's own exile zone. Gate: only active
        // while Ketramose is on the battlefield (CR 603.6e).
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            int ExileCount()
            {
                if (exileCardCount != null) return exileCardCount();
                return card.Controller?.Zones.Exile.GetCards().Count() ?? 0;
            }

            bool LockedForCombat(Creature queried)
            {
                if (!ReferenceEquals(queried, card)) return false; // self-scoped
                return ExileCount() < ExileUnlockThreshold;
            }

            bool OnBattlefield() => card.Zone == ZoneType.Battlefield;

            continuousEffects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotAttack,
                predicate: LockedForCombat,
                isActiveGate: OnBattlefield,
                expiresAtEndOfTurn: false));

            continuousEffects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotBlock,
                predicate: LockedForCombat,
                isActiveGate: OnBattlefield,
                expiresAtEndOfTurn: false));
        }

        // ----------------------------------------------------------------
        // "Whenever one or more cards are put into exile from graveyards
        // and/or the battlefield during your turn, you draw a card and lose
        // 1 life." CR 603.1 / 603.2 (triggered ability) / 603.3e ("one or
        // more" batch).
        //
        // Listens to CardMovedEvent filtered to ToZone == Exile with
        // FromZone ∈ {Graveyard, Battlefield}, gated on the controller's
        // turn. v1 fires per-card (the engine raises a CardMovedEvent per
        // moved card); the "one or more … batch" coalescing is deferred (see
        // the class doc) — same posture as the rest of the "one or more"
        // trigger pool.
        // ----------------------------------------------------------------
        bool ControllersTurn() => isControllersTurn?.Invoke() ?? true;

        var drawAndLose = new Effect(
            $"{CardName}: draw a card and lose {LifeLossAmount} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, DrawAmount);
                Fx.LoseLife(controller, LifeLossAmount);
            });

        var exileTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Exile
                && (e.FromZone == ZoneType.Graveyard || e.FromZone == ZoneType.Battlefield)
                && ControllersTurn()),
            effects: new IEffect[] { drawAndLose },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(exileTrigger);
        triggers?.RegisterTriggeredAbility(exileTrigger);

        return card;
    }
}
