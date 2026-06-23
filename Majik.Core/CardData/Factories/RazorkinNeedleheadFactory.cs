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
/// Named-card factory for Razorkin Needlehead (Duskmourn: House of Horror,
/// {R}{R}).
///
/// Creature — Human Assassin 2/2. Oracle text (verified against the embedded
/// Scryfall seed 2026-06-23):
///   "This creature has first strike during your turn.
///    Whenever an opponent draws a card, this creature deals 1 damage to them."
///
/// ## Shape source
/// Card identity (name, {R}{R}, 2/2, Creature — Human Assassin) is loaded from
/// <c>Majik.Core/CardData/Cards/razorkin-needlehead.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The two abilities below are attached in
/// code (one conditional Layer 6 keyword static, one opponent-draw trigger).
///
/// ## Implemented
///
/// - <b>2/2 Creature — Human Assassin at {R}{R}.</b>
///
/// - <b>"This creature has first strike during your turn." (CR 613.1f / CR
///   611.2c).</b> A conditional Layer 6 (ability-adding) keyword static,
///   implemented via the reusable
///   <see cref="WhileControllersTurnKeywordStaticEffect"/> — the Layer 6 keyword
///   sibling of <see cref="WhileControllersTurnPumpEffect"/> (Skophos Reaver's
///   "during your turn, +2/+0"). The "First strike" grant appears the instant
///   the active player becomes this creature's controller (CR 500.1) and lifts
///   the instant the turn passes; the gate lives in
///   <see cref="WhileControllersTurnKeywordStaticEffect.AppliesTo"/> so
///   <see cref="ContinuousEffectsService.Prune"/> never permanently drops it.
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFirstStrike"/> reads the
///   granted keyword off the computed working set when an effects service is
///   wired, so first-strike combat-damage assignment (CR 510.5 / CR 702.7c)
///   honours the grant during the controller's turn only.
///
/// - <b>"Whenever an opponent draws a card, this creature deals 1 damage to
///   them." (CR 603.1 / CR 119.3 / CR 109.5).</b> A hand-rolled
///   <see cref="TriggeredAbility"/> over <see cref="CardDrawnEvent"/> filtered
///   to a drawer OTHER than this creature's current controller (every other
///   player is an opponent — CR 102.2). The firing predicate captures the
///   drawing opponent in a shared cell; the resolve body deals 1 damage to that
///   captured player via <see cref="Fx.DealDamage(object, int)"/> (routes to
///   <see cref="Player.LoseLife"/>, incrementing LifeLostThisTurn so downstream
///   observers see the loss). Untargeted — "them" identifies the drawer, no
///   <see cref="TargetRequest"/> is read (the declarative-effect analogue is
///   <c>deal_damage_to_triggering_player</c>; this hand-rolls the same shape
///   because no opponent-draw JSON trigger type exists yet). Unlike Orcish
///   Bowmasters there is NO "first draw is free" exception — EVERY opponent
///   draw fires, so no per-step counter is needed.
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. No live continuous-effects
///   service, so the conditional first-strike static is not registered (the
///   card is the correct base 2/2 with no first strike). The opponent-draw
///   trigger is still attached to the card's abilities for shape observability
///   but never fires without a live <see cref="TriggerManager"/>. Suitable for
///   factory-shape / dispatch tests.
/// - <see cref="Create(Player, ContinuousEffectsService)"/> — the
///   source-generated effects-aware overload (see <see cref="NamedCardFactory"/>):
///   registers the conditional first-strike static on the supplied service so
///   the keyword surfaces during the controller's turn. The opponent-draw
///   trigger is attached to the card's abilities in BOTH overloads; the live
///   engine's <see cref="TriggerManager"/> auto-binds any card with triggered
///   abilities when it crosses a zone boundary and registers triggers whose
///   <c>ActiveZones</c> include the card's current zone, so no explicit bus /
///   trigger-manager parameter is needed on this factory.
/// </summary>
[CardName("Razorkin Needlehead")]
public static class RazorkinNeedleheadFactory
{
    public const string CardName = "Razorkin Needlehead";

    /// <summary>Keyword granted during the controller's turn (CR 702.7).</summary>
    public const string OwnTurnKeyword = "First strike";

    /// <summary>Damage dealt to an opponent each time they draw (CR 119.3).</summary>
    public const int DamagePerOpponentDraw = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("razorkin-needlehead");

    /// <summary>
    /// Construct Razorkin Needlehead with no live continuous-effects wiring. The
    /// conditional first-strike static is NOT registered — the card is the
    /// correct base 2/2 with no first strike. The opponent-draw trigger is
    /// attached for shape observability but never fires. Suitable for
    /// factory-shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Razorkin Needlehead. When <paramref name="effects"/> is supplied
    /// (the source-generated effects-aware dispatch path) the "first strike
    /// during your turn" static registers so the keyword surfaces during the
    /// controller's turn. The opponent-draw trigger is attached to the card's
    /// abilities in both overloads (the live engine auto-registers it).
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "This creature has first strike during your turn." — CR 613.1f /
        // CR 611.2c. Conditional Layer 6 keyword static whose gate re-reads
        // the live active player on every Compute (mirrors Skophos Reaver's
        // "during your turn" pump, swapping +P/+T for a keyword grant).
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;
            effects.Register(new WhileControllersTurnKeywordStaticEffect(
                card,
                OwnTurnKeyword,
                isControllersTurn: () =>
                    effects.ActivePlayer != null
                    && ReferenceEquals(effects.ActivePlayer, card.Controller)));
        }

        // ----------------------------------------------------------------
        // "Whenever an opponent draws a card, this creature deals 1 damage
        // to them." — CR 603.1 / CR 119.3 / CR 109.5. Hand-rolled trigger
        // over CardDrawnEvent filtered to a drawer != current controller
        // (every other player is an opponent — CR 102.2). The firing
        // predicate captures the drawing opponent; the resolve body deals 1
        // damage to that captured player (untargeted — "them" identifies the
        // drawer). No "first draw is free" exception, unlike Orcish
        // Bowmasters — every opponent draw fires, so no per-step counter.
        // ----------------------------------------------------------------
        var pendingTarget = new Player?[] { null };

        var drawCondition = new EventTriggerCondition<CardDrawnEvent>((e, _) =>
        {
            var drawer = e.Player;
            if (drawer == null) return false;
            if (ReferenceEquals(drawer, card.Controller ?? owner)) return false;
            pendingTarget[0] = drawer;
            return true;
        });

        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: drawCondition,
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: deal {DamagePerOpponentDraw} damage to the drawing opponent",
                    () =>
                    {
                        var target = pendingTarget[0];
                        pendingTarget[0] = null;
                        if (target == null) return;
                        // CR 119.3 — 1 damage to the drawing opponent. Routed
                        // through Fx.DealDamage(player) → Player.LoseLife so
                        // LifeLostThisTurn increments for downstream observers.
                        Fx.DealDamage(target, DamagePerOpponentDraw);
                    }),
            },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drawTrigger);

        return card;
    }
}
