using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the PULL half of the split card Push // Pull
/// (Strixhaven: School of Mages, {1}{W/B} // {4}{B/R}{B/R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-02):
///   "Put up to two target creature cards from a single graveyard onto the
///    battlefield under your control. They gain haste until end of turn.
///    Sacrifice them at the beginning of the next end step."
///
/// Sister half — <see cref="PushFactory"/> ({1}{W/B}; "Destroy target tapped
/// creature.").
///
/// ## Split-card modelling (CR 712 / CR 709)
///
/// A split card is a single physical card with two halves; the caster picks
/// one half on cast and casts only that half (CR 712.4a). v1 models each
/// printed half as its own <c>[CardName]</c>-dispatched factory (same posture
/// as Wear // Tear). Pull is the back half; it carries an <see cref="MdfcState"/>
/// face tracker (front = "Push", back = "Pull").
///
/// Pull is the multi-target reanimation sibling of
/// <see cref="FootstepsOfTheGoryoFactory"/>. The differences from Footsteps:
///   - <b>Up to two</b> target creature cards (0..2 — CR 115.1a), not one.
///   - <b>From a single graveyard</b>: both chosen cards must come from the
///     same player's graveyard (CR 608.2b enforced at resolution; the
///     candidate gatherer surfaces every graveyard's creature cards, and the
///     prompt picks at most two — the engine has no cross-target "same
///     graveyard" gate primitive yet, so the resolve body enforces it by
///     dropping any second pick from a different graveyard, see Deferred).
///   - <b>Haste until end of turn</b> on each reanimated creature
///     (CR 702.10 — like Goryo's Vengeance).
///
/// ## Implemented (v1)
/// - Sorcery shape at printed cost {4}{B/R}{B/R} (hybrid black/red — both
///   colours derived from the hybrid pips per CR 202.2 / CR 709.4), built from
///   the embedded JSON def (<c>pull.json</c>).
/// - <see cref="MdfcState"/> attached (back = Pull).
/// - <see cref="BuildSpellDefinition"/> — a single 0..2 "target creature card"
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Reanimate"/>).
///   The candidate gatherer yields creature cards in any player's graveyard.
///   On resolution each target is re-checked per CR 608.2b (must still be a
///   creature card in a graveyard); the FIRST valid pick fixes the "single
///   graveyard" so a second pick from a different graveyard is dropped.
/// - "...onto the battlefield under your control" — each returned creature
///   enters under the caster's control (CR 110.2) via
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> (ZoneService-routed when
///   supplied so ETB triggers fire — CR 603.6a).
/// - "They gain haste until end of turn." (CR 702.10) — a Layer 6 keyword
///   grant via <see cref="GrantKeywordUntilEndOfTurnEffect"/> on each
///   reanimated creature's <see cref="Creature.ActiveEffects"/> (no-op when no
///   continuous-effects service is wired — shape mode); summoning sickness is
///   also cleared (CR 702.10b).
/// - "Sacrifice them at the beginning of the next end step." (CR 603.7) — when
///   a <see cref="TriggerManager"/> is supplied, ONE delayed triggered ability
///   is registered that fires on the first <see cref="StepStartedEvent"/> with
///   <see cref="PhaseStateType.End"/> strictly after this resolution and
///   sacrifices every still-on-battlefield reanimated creature (CR 701.16 →
///   <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///   <see cref="ZoneMoveReason.Sacrifice"/>, so indestructible is bypassed).
///
/// ## Deferred (v1 gaps)
/// - <b>Cross-target "single graveyard" gate at TARGETING time</b>: the engine
///   has no primitive that constrains the second target's legal set by the
///   first target's graveyard. v1 enforces it at RESOLUTION (the resolve body
///   drops a second pick from a different graveyard). Real prompt-time gating
///   awaits a multi-target dependency primitive. Same shape posture as
///   Footsteps' single-target prompt deferral.
/// - <b>Shape-only callers</b>: passing <c>triggers: null</c> performs the
///   reanimation + haste grant but skips the delayed sacrifice.
/// </summary>
[CardName("Pull")]
public static class PullFactory
{
    public const string CardName = "Pull";
    public const string SisterName = "Push";
    public const string Slug = "pull";
    public const string PrintedManaCost = "{4}{B/R}{B/R}";

    /// <summary>Granted keyword. CR 702.10 — Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// Materialise the Pull half (Sorcery, {4}{B/R}{B/R}) from the embedded
    /// JSON def, with the <see cref="MdfcState"/> face tracker attached
    /// (back = Pull). Resolve behaviour is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(def, owner);

        // CR 712 — split-card face tracker. Pull is the back half, so flip the
        // tracker to the back face (front = Push, back = Pull). Informational.
        var state = new MdfcState(SisterName, CardName);
        state.Transform(); // flip to the back half (Pull)
        card.MdfcState = state;
        return card;
    }

    /// <summary>
    /// Build the "put up to two target creature cards from a single graveyard
    /// onto the battlefield under your control; they gain haste; sacrifice them
    /// at the next end step" <see cref="SpellDefinition"/>.
    /// </summary>
    /// <param name="caster">Spell controller — the battlefield the creatures
    /// enter under (CR 110.2) and the delayed-trigger controller.</param>
    /// <param name="resolver">Maps each agent-supplied raw target token to the
    /// live engine object. Pass <c>o =&gt; o</c> for tests that hand cards
    /// directly.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield moves (and the end-step sacrifice) route through
    /// <see cref="ZoneService.MoveCard"/> so ETB / LTB triggers fire
    /// (CR 603.6a).</param>
    /// <param name="triggers">Optional. When supplied the delayed end-step
    /// sacrifice trigger is registered (CR 603.7). Shape-only callers can pass
    /// null — the reanimation + haste still happen but the creatures are not
    /// sacrificed automatically.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService = null,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    // "up to two target creature cards from a single graveyard"
                    Description: "up to two target creature cards from a single graveyard",
                    MinTargets: 0,
                    MaxTargets: 2,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate,
                    // Creature cards in any graveyard. The "single graveyard"
                    // constraint is enforced at resolution (see Resolve).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Graveyard.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: put up to two creature cards from a single graveyard onto the battlefield under your control; haste; sacrifice next end step",
                    () => Resolve(caster, chosen, resolver, zoneService, triggers)),
            });
    }

    /// <summary>
    /// Resolve the reanimation + haste grant + register the single delayed
    /// end-step sacrifice. CR 608.2b — each target must still be a creature
    /// card in a graveyard; the first valid pick fixes the "single graveyard"
    /// so a later pick from a different graveyard is dropped.
    /// </summary>
    private static void Resolve(
        Player caster,
        ChosenSpellParams chosen,
        Func<object, object> resolver,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        if (chosen.Targets.Count == 0) return;

        var reanimated = new List<Creature>();
        Player? sourceGraveyard = null;

        foreach (var raw in chosen.Targets[0])
        {
            var live = resolver(raw);

            // CR 608.2b — illegal-on-resolution gate: must be a creature card
            // still in a graveyard.
            if (live is not Creature creature) continue;
            if (creature.Zone != ZoneType.Graveyard) continue;
            if (creature.Owner == null) continue;

            // "from a single graveyard" — the first valid pick fixes the
            // graveyard; a later pick from a different graveyard is dropped
            // (CR 608.2b). The chosen targets should already share a graveyard
            // when picked by a real agent; this is the resolution-time guard.
            sourceGraveyard ??= creature.Owner;
            if (!ReferenceEquals(creature.Owner, sourceGraveyard)) continue;

            // "...onto the battlefield under your control" — CR 110.2: the
            // permanent enters under the caster's control. ZoneService-routed
            // when supplied so ETB triggers fire (CR 603.6a).
            Fx.ReturnFromGraveyardToBattlefield(creature, caster, zoneService);

            // "They gain haste until end of turn." (CR 702.10) — Layer 6
            // keyword grant. No-op silently when no ActiveEffects service is
            // wired (shape mode). Haste also clears summoning sickness so the
            // creature is attack-ready immediately (CR 702.10b).
            if (creature.ActiveEffects != null)
            {
                creature.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(creature, GrantedKeyword));
            }
            creature.HasSummoningSickness = false;

            reanimated.Add(creature);
        }

        // "Sacrifice them at the beginning of the next end step." (CR 603.7) —
        // ONE delayed triggered ability that sacrifices every still-on-
        // battlefield reanimated creature on the first End step strictly after
        // this resolution (timestamp fence mirrors Footsteps / Goryo's
        // Vengeance). Shape-only callers (triggers == null) skip the cleanup.
        if (triggers == null || reanimated.Count == 0) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var sacrificeEffect = new Effect(
            $"{CardName}: sacrifice reanimated creatures at next end step",
            () =>
            {
                foreach (var creature in reanimated)
                {
                    // Zone-check at fire time so a creature that already left
                    // the battlefield (bounce, destroy, etc.) is not yanked
                    // from elsewhere.
                    if (creature.Zone != ZoneType.Battlefield) continue;

                    // CR 701.16 — sacrifice: owner's battlefield → owner's
                    // graveyard. Bypasses indestructible (CR 702.12b).
                    Fx.MoveToGraveyard(creature, ZoneMoveReason.Sacrifice);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: caster,
            controller: caster,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { sacrificeEffect });

        triggers.RegisterDelayed(delayed);
    }
}
