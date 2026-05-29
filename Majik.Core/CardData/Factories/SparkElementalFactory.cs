using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spark Elemental (Tenth Edition, {R}).
///
/// Creature — Elemental 3/1. Oracle text (verified against Scryfall):
///   "Trample, haste
///    At the beginning of the end step, sacrifice this creature."
///
/// The base shape (name, Creature, Elemental subtype, {R}, 3/1) is
/// materialised from the embedded JSON definition
/// (<c>spark-elemental.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed keywords and
/// the end-step self-sacrifice trigger are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express keyword markers or
/// triggered abilities, so they live in the factory (same posture as the
/// other JSON-backed cards whose behaviour outgrows the schema, e.g.
/// <see cref="StormscaleScionFactory"/> / <see cref="VexingDevilFactory"/>).
///
/// This is the classic "Ball Lightning" template — a cheap, oversized red
/// body with evasion + haste that swings once and then sacrifices itself at
/// the end step.
///
/// ## Implemented (v1)
/// - <b>Creature — Elemental {R} 3/1</b>, owner / controller stamped.
/// - <b>Trample (CR 702.19)</b> + <b>Haste (CR 702.10)</b> — wired as
///   <see cref="KeywordAbility"/> markers so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> /
///   <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/> surface the
///   combat / summoning-sickness properties. Same shape as
///   <see cref="StormscaleScionFactory"/>'s Flying marker.
/// - <b>End-step self-sacrifice (CR 603 + CR 701.16)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="StepStartedEvent"/>
///   firing on <see cref="Majik.Core.StateMachine.PhaseStateType.End"/>.
///   The printed wording — "At the beginning of the end step" — is
///   <i>unscoped</i>, so per CR 603.3d it triggers on <b>every</b> player's
///   end step, not just the controller's. The condition therefore matches
///   on step type alone (no controller filter — distinct from
///   <see cref="Triggers.OnStepBegin"/>, which is controller-scoped for the
///   "your end step" family). On resolution the Elemental is sacrificed
///   (battlefield → owner's graveyard, Sacrifice reason so Indestructible /
///   regeneration gates don't apply — CR 701.16).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape + keywords + the end-step trigger
///   attached structurally (not enrolled with a <see cref="TriggerManager"/>);
///   the resolve body still sacrifices via <see cref="Fx.Sacrifice"/>.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully
///   wired. The trigger enrolls with <paramref name="triggers"/>; the
///   sacrifice routes through <paramref name="zoneService"/> when supplied
///   so LTB / zone-change events fire (CR 603.6a).
/// </summary>
[CardName("Spark Elemental")]
public static class SparkElementalFactory
{
    public const string CardName = "Spark Elemental";
    public const string Slug = "spark-elemental";
    public const int Power = 3;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Spark Elemental with no runtime service wiring. The card
    /// has the correct shape (Elemental 3/1 at {R}), Trample + Haste, and
    /// the end-step trigger is attached for structural / dispatcher
    /// inspection (not enrolled with a <see cref="TriggerManager"/>). The
    /// resolve body still sacrifices via <see cref="Fx.Sacrifice"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Spark Elemental with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for end-step registration. May
    /// be null — the trigger is attached structurally but not enrolled.</param>
    /// <param name="zoneService">Zone service the sacrifice routes through so
    /// LTB / zone-change events fire. May be null — raw-zone sacrifice path
    /// via <see cref="Fx.Sacrifice"/>.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental subtype, {R}, 3/1). The JSON carries no abilities — the
        // keywords + end-step trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.19 — Trample; CR 702.10 — Haste. KeywordAbility markers so
        // CombatAbilities surfaces excess-damage assignment + the
        // can-attack-the-turn-it-enters property.
        card.AddAbility(new KeywordAbility("Trample", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // End-step self-sacrifice — CR 603 + CR 701.16.
        //   "At the beginning of the end step, sacrifice this creature."
        // Unscoped wording (CR 603.3d): fires on EVERY player's end step, so
        // the condition matches on step type alone (no controller filter).
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
                e.StepType == Majik.Core.StateMachine.PhaseStateType.End),
            effects: new IEffect[] { sacrificeEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        return card;
    }
}
