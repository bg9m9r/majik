using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lightning Skelemental (Modern Horizons 2, {B}{R}{R}).
///
/// Creature — Elemental Skeleton 6/1. Oracle text (verified against Scryfall):
///   "Trample, haste
///    Whenever this creature deals combat damage to a player, that player
///    discards two cards.
///    At the beginning of the end step, sacrifice this creature."
///
/// ## Shape source
///
/// Card identity (name, {B}{R}{R}, 6/1, Creature — Elemental Skeleton) is
/// materialised from the embedded JSON definition
/// (<c>lightning-skelemental.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed keywords, the
/// combat-damage discard trigger, and the end-step self-sacrifice trigger are
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express keyword markers, a "damaged player discards N" effect, or an
/// unscoped end-step sacrifice trigger, so those live in the factory (same
/// posture as <see cref="SparkElementalFactory"/> and the other JSON-backed
/// cards whose behaviour outgrows the schema).
///
/// This is the classic "Ball Lightning" template (cf.
/// <see cref="SparkElementalFactory"/> / <see cref="HellsparkElementalFactory"/>)
/// — a cheap, oversized body with trample + haste that swings once and then
/// sacrifices itself at the end step — with an added combat-damage discard
/// rider.
///
/// ## Implemented (v1)
/// - <b>Creature — Elemental Skeleton {B}{R}{R} 6/1</b>, owner / controller
///   stamped.
/// - <b>Trample (CR 702.19)</b> + <b>Haste (CR 702.10)</b> — wired as
///   <see cref="KeywordAbility"/> markers so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> surface the
///   combat / summoning-sickness properties. Same marker pattern as
///   <see cref="SparkElementalFactory"/>.
/// - <b>"Whenever this creature deals combat damage to a player, that player
///   discards two cards" (CR 510 / CR 603.1)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="CombatDamageDealtEvent"/>
///   matching when the damage <see cref="CombatDamageDealtEvent.Source"/> is
///   this creature AND the <see cref="DamageDealtEvent.TargetPlayer"/> is
///   non-null. On resolution the damaged player discards two cards. v1 uses a
///   deterministic first-two-cards-in-hand pick (same v1 discard policy as
///   <see cref="SwordOfFeastAndFamineFactory"/>'s discard half — agent-driven
///   "the damaged player chooses" per CR 701.16a is deferred behind the shared
///   discard-prompt queue). Source + non-null-TargetPlayer predicate mirrors
///   <see cref="GrimFlayerFactory"/>'s self-sourced combat-damage trigger.
/// - <b>End-step self-sacrifice (CR 603 + CR 701.16)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="StepStartedEvent"/> firing
///   on <see cref="Majik.Core.StateMachine.StepStateType.End"/>. The printed
///   wording — "At the beginning of the end step" — is <i>unscoped</i>, so per
///   CR 603.3d it triggers on <b>every</b> player's end step (the condition
///   matches on step type alone, no controller filter — byte-identical to
///   <see cref="SparkElementalFactory"/>). On resolution the Elemental is
///   sacrificed (battlefield → owner's graveyard, Sacrifice reason so
///   Indestructible / regeneration gates don't apply — CR 701.16).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + keywords + both triggers attached
///   structurally (not enrolled with a <see cref="TriggerManager"/>); the
///   sacrifice resolve body still sacrifices via <see cref="Fx.Sacrifice"/>.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. Both triggers enroll with <paramref name="triggers"/>; the
///   sacrifice routes through <paramref name="zoneService"/> when supplied so
///   LTB / zone-change events fire (CR 603.6a).
/// </summary>
[CardName("Lightning Skelemental")]
public static class LightningSkelementalFactory
{
    public const string CardName = "Lightning Skelemental";
    public const string Slug = "lightning-skelemental";
    public const int Power = 6;
    public const int Toughness = 1;

    /// <summary>Number of cards the damaged player discards (CR 510 / 603.1).</summary>
    public const int DiscardCount = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Lightning Skelemental with no runtime service wiring. The
    /// card has the correct shape (Elemental Skeleton 6/1 at {B}{R}{R}),
    /// Trample + Haste, and both triggers are attached for structural /
    /// dispatcher inspection (not enrolled with a
    /// <see cref="TriggerManager"/>). The sacrifice resolve body still
    /// sacrifices via <see cref="Fx.Sacrifice"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Lightning Skelemental with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for combat-damage + end-step
    /// registration. May be null — the triggers are attached structurally but
    /// not enrolled.</param>
    /// <param name="zoneService">Zone service the sacrifice routes through so
    /// LTB / zone-change events fire. May be null — raw-zone sacrifice path via
    /// <see cref="Fx.Sacrifice"/>.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental + Skeleton subtypes, {B}{R}{R}, 6/1). The JSON carries no
        // abilities — the keywords + both triggers are layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample; CR 702.10 — Haste. KeywordAbility markers so
        // CombatAbilities surfaces excess-damage assignment + the
        // can-attack-the-turn-it-enters property.
        card.AddAbility(new KeywordAbility("Trample", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // "Whenever this creature deals combat damage to a player, that
        // player discards two cards." CR 510 + CR 603.1.
        //
        // The predicate captures the damaged player off the event so the
        // resolved effect targets the correct hand at fire time. CR 603.3
        // evaluates the trigger condition before the ability hits the stack,
        // so the captured player is fresh by the time the effect resolves
        // (same capture pattern as SwordOfFeastAndFamineFactory). Self-sourced
        // (Source == this creature), matching GrimFlayerFactory's predicate.
        // ----------------------------------------------------------------
        Player? capturedDamaged = null;

        var discardEffect = new Effect(
            $"{CardName}: damaged player discards {DiscardCount} cards",
            () =>
            {
                // CR 701.16a — the damaged player discards. v1 deterministic
                // first-cards-in-hand pick (agent "you choose which cards you
                // discard" deferred behind the shared discard-prompt queue,
                // same v1 policy as Sword of Feast and Famine).
                var victim = capturedDamaged;
                if (victim == null) return;

                for (var i = 0; i < DiscardCount; i++)
                {
                    var pick = victim.Zones.Hand.GetCards().FirstOrDefault();
                    if (pick == null) break; // fewer than two cards in hand
                    victim.Zones.Hand.RemoveCard(pick);
                    victim.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }
            });

        var discardTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false; // damage to a player only
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { discardEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(discardTrigger);
        triggers?.RegisterTriggeredAbility(discardTrigger);

        // ----------------------------------------------------------------
        // End-step self-sacrifice — CR 603 + CR 701.16.
        //   "At the beginning of the end step, sacrifice this creature."
        // Unscoped wording (CR 603.3d): fires on EVERY player's end step, so
        // the condition matches on step type alone (no controller filter).
        // Byte-identical to SparkElementalFactory.
        // ----------------------------------------------------------------
        var sacrificeEffect = new Effect(
            $"{CardName}: at the beginning of the end step, sacrifice this creature",
            () =>
            {
                // Only sacrifice while still on the battlefield (it may have
                // already left — e.g. died in combat earlier the same turn).
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 701.16 — sacrifice (battlefield → graveyard, Sacrifice
                // reason so Indestructible / regeneration gates don't apply).
                if (zoneService != null)
                {
                    zoneService.MoveCard(
                        card, ZoneType.Battlefield, ZoneType.Graveyard, card.Controller ?? owner);
                }
                else
                {
                    Fx.Sacrifice(card);
                }
            });

        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            // Unscoped "the end step" — fires on any player's end step
            // (CR 603.3d). Match on step type alone, no controller filter.
            condition: new EventTriggerCondition<StepStartedEvent>((e, _) =>
                e.StepType == Majik.Core.StateMachine.StepStateType.End),
            effects: new IEffect[] { sacrificeEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        return card;
    }
}
