using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ball Lightning (Mirage, {R}{R}{R}).
///
/// Creature — Elemental 6/1. Oracle text (verified against Scryfall):
///   "Trample
///    Haste
///    At the beginning of the end step, sacrifice this creature."
///
/// The base shape (name, Creature, Elemental subtype, {R}{R}{R}, 6/1) is
/// materialised from the embedded JSON definition (<c>ball-lightning.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed keyword markers
/// and the end-step self-sacrifice trigger are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't yet express keyword markers or a
/// "beginning of the end step → sacrifice this" trigger (same posture as
/// <see cref="ArclightPhoenixFactory"/>, which layers Flying/Haste markers +
/// a step-begin trigger over its JSON shape).
///
/// ## Implemented (v1)
/// - 6/1 Creature — Elemental, mana cost {R}{R}{R}, owner / controller stamped.
/// - <b>Trample</b> (CR 702.19) + <b>Haste</b> (CR 702.10) as
///   <see cref="KeywordAbility"/> markers.
/// - <b>End-step self-sacrifice trigger (CR 603.6a / CR 603.3a)</b>: a
///   <see cref="TriggeredAbility"/> firing on <see cref="StepStartedEvent"/>
///   for <see cref="StepStateType.End"/>. The printed clause has no
///   possessive ("the end step"), so the trigger fires on the FIRST end step
///   after the creature is on the battlefield — on ANY player's turn, not
///   just its controller's (the condition is therefore NOT filtered by
///   player). On resolution the creature is sacrificed (CR 701.16 —
///   battlefield → its owner's graveyard, Sacrifice reason so
///   Indestructible / regeneration gates don't apply).
///
/// ## Bot intent
/// A {R}{R}{R} 6/1 with haste + trample that attacks once for (typically) 6
/// then sacrifices itself at end of turn — a one-shot burst-damage threat.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The keyword markers + end-step
///   trigger are attached structurally, but the trigger is not registered with
///   any <see cref="TriggerManager"/>. This is the overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?, ZoneService?)"/> — fully wired.
///   The end-step trigger registers with <paramref name="triggers"/>; the
///   sacrifice routes through <paramref name="zoneService"/> when supplied so
///   LTB / zone-change events fire (CR 603.6a).
/// </summary>
[CardName("Ball Lightning")]
public static class BallLightningFactory
{
    public const string CardName = "Ball Lightning";
    public const string Slug = "ball-lightning";

    /// <summary>
    /// Construct Ball Lightning with no runtime service wiring. The card has
    /// the correct shape (name, Creature, Elemental, {R}{R}{R}, 6/1) plus the
    /// Trample / Haste markers and the end-step trigger for structural /
    /// dispatcher inspection, but the trigger is not registered with a
    /// <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Effects-aware overload the source generator recognises and the
    /// <b>production</b> <c>GameFacade</c> routed build dispatches to (via
    /// <see cref="NamedCardFactory.Create(string, Player, Effects.ContinuousEffectsService?)"/>).
    /// Ball Lightning registers no continuous effect, but its end-step
    /// self-sacrifice (CR 701.16) must publish a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) so aristocrat
    /// "whenever a creature you control is sacrificed / whenever an opponent
    /// sacrifices…" payoffs (Mayhem Devil, It That Betrays) see it — so this
    /// forwards the bus from <c>effects.EventBus</c> into the sac closure.
    /// Without this overload the routed build falls through to single-arg
    /// dispatch and the self-sacrifice would publish nothing (the class-(b)
    /// sac-bus pay-down — same fix as Sakura-Tribe Elder).
    /// </summary>
    public static Creature Create(Player owner, Effects.ContinuousEffectsService? effects) =>
        Create(owner, triggers: null, zoneService: null, eventBus: effects?.EventBus);

    /// <summary>
    /// Construct Ball Lightning with full runtime wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager for end-step registration. May be
    /// null — the trigger is attached structurally but not enrolled.</param>
    /// <param name="zoneService">Zone service the sacrifice routes through so
    /// LTB / zone-change events fire. May be null — raw-zone sacrifice path.</param>
    /// <param name="eventBus">Bus the self-sacrifice publishes
    /// <see cref="PermanentSacrificedEvent"/> on (CR 701.16a). When supplied the
    /// sac routes through <see cref="Fx.Sacrifice(ICard, Player, IEventBus)"/>;
    /// null falls back to the publish-nothing path (shape / direct-call).</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elemental subtype, {R}{R}{R}, 6/1). The JSON carries no abilities —
        // the keyword markers + end-step trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.19 — Trample. CR 702.10 — Haste.
        card.AddAbility(new KeywordAbility("Trample", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // End-step self-sacrifice trigger — CR 603.6a / CR 603.3a.
        //   "At the beginning of the end step, sacrifice this creature."
        // No possessive in the printed clause → fires on the FIRST end step
        // after the creature is on the battlefield, on ANY player's turn.
        // The condition is therefore not filtered by player.
        // ----------------------------------------------------------------
        var sacrificeEffect = new Effect(
            $"{CardName}: sacrifice this creature at the beginning of the end step",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 701.16 — sacrifice (battlefield → owner's graveyard,
                // Sacrifice reason so Indestructible / regeneration gates don't
                // apply). When a bus is supplied (the prod effects-aware build)
                // route through Fx.Sacrifice(perm, player, bus) so a
                // PermanentSacrificedEvent fires (CR 701.16a) crediting the
                // controller as the sacrificing player — the seam aristocrat
                // payoffs read.
                if (eventBus != null)
                {
                    Fx.Sacrifice(card, card.Controller ?? owner, eventBus);
                }
                else if (zoneService != null)
                {
                    zoneService.MoveCard(
                        card, ZoneType.Battlefield, ZoneType.Graveyard, owner);
                }
                else
                {
                    Fx.Sacrifice(card);
                }
            });

        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == StepStateType.End),
            effects: new IEffect[] { sacrificeEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        return card;
    }
}
