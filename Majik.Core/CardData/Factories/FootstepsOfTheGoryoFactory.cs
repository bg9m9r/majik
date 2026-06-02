using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
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
/// Named-card factory for Footsteps of the Goryo (Betrayers of Kamigawa, {2}{B}).
///
/// Sorcery — Arcane. Oracle text (verified against Scryfall 2026-06-02):
///   "Return target creature card from your graveyard to the battlefield.
///    Sacrifice that creature at the beginning of the next end step."
///
/// Footsteps of the Goryo is the Sorcery sibling of
/// <see cref="GoryosVengeanceFactory"/>: both reanimate a creature card from
/// the caster's graveyard and clean it up at the next end step. The
/// differences from Goryo's Vengeance are:
///   - <b>Any creature card</b>, not just legendary ("target creature card").
///   - <b>No Haste grant</b> (Footsteps grants no keyword).
///   - <b>Sacrifice</b> at the next end step (CR 701.16), not exile —
///     so the creature lands in its owner's graveyard, and indestructible
///     does not save it (CR 702.12b).
///
/// The "return target creature card from your graveyard" body is shared with
/// <see cref="UnburialRitesFactory"/> (same <see cref="Fx.ReturnFromGraveyardToBattlefield"/>
/// path, caster-graveyard-scoped target, no life loss). The delayed end-step
/// cleanup mirrors <see cref="BerserkFactory"/> / <see cref="GoryosVengeanceFactory"/>
/// (one-shot <see cref="DelayedTriggeredAbility"/>, CR 603.7), but uses
/// <see cref="ZoneMoveReason.Sacrifice"/> instead of destroy/exile.
///
/// ## Card identity comes from JSON
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>footsteps-of-the-goryo.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="UnburialRitesFactory"/>. The printed Arcane subtype is omitted
/// from the runtime card: CR 205.3 — Arcane is a spell subtype the engine's
/// <see cref="CardSubtype"/> enum carries, but Footsteps has no Splice
/// interaction, so it is left off the JSON (same posture as Unburial Rites).
///
/// ## Implemented (v1)
/// - Sorcery shape at printed cost {2}{B}, owner / controller wired from JSON.
/// - <see cref="BuildSpellDefinition"/> — a single 1..1 "target creature card
///   in your graveyard" <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Reanimate"/>). The candidate gatherer yields creature
///   cards in the caster's graveyard only ("your graveyard"). On resolution
///   the target is re-checked per CR 608.2b (must still be a creature card in
///   the caster's graveyard); on success it is returned to the caster's
///   battlefield via <see cref="Fx.ReturnFromGraveyardToBattlefield"/>
///   (ZoneService-routed when supplied so ETB triggers fire — CR 603.6a).
/// - "Sacrifice that creature at the beginning of the next end step"
///   (CR 603.7): when a <see cref="TriggerManager"/> is supplied, a one-shot
///   <see cref="DelayedTriggeredAbility"/> is registered that fires on the
///   first <see cref="StepStartedEvent"/> with <see cref="PhaseStateType.End"/>
///   strictly after this resolution (timestamp fence mirrors Goryo's
///   Vengeance / Berserk). On fire, if the creature is still on the
///   battlefield it is sacrificed (CR 701.16 →
///   <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///   <see cref="ZoneMoveReason.Sacrifice"/>, so indestructible is bypassed).
///   Zone-check at fire time so a creature that already left the battlefield
///   (bounce, destroy, etc.) is not yanked from elsewhere.
///
/// ## Relevant rules
/// - CR 701.20 — return a card from a graveyard to the battlefield.
/// - CR 110.2 — a permanent enters under the control of the player who put it
///   onto the battlefield.
/// - CR 603.6a — ETB triggers fire on the returned creature.
/// - CR 608.2b — illegal target at resolution → no-op.
/// - CR 603.7 — delayed triggered ability (the end-step sacrifice).
/// - CR 701.16 — sacrifice (owner's battlefield → owner's graveyard).
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt</b>: the live cast flow supplies the chosen
///   target through <see cref="ChosenSpellParams.Targets"/>; the resolver maps
///   tokens to live cards. Same posture as <see cref="UnburialRitesFactory"/>.
/// - <b>Shape-only callers</b>: passing <c>triggers: null</c> performs the
///   reanimation but skips the delayed sacrifice (nothing to clean up
///   automatically).
/// </summary>
[CardName("Footsteps of the Goryo")]
public static class FootstepsOfTheGoryoFactory
{
    public const string CardName = "Footsteps of the Goryo";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "footsteps-of-the-goryo";

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {2}{B}) from the
    /// embedded JSON definition. Resolve behaviour ("return target creature
    /// card from your graveyard" + delayed sacrifice) is built on demand via
    /// <see cref="BuildSpellDefinition"/>, mirroring
    /// <see cref="UnburialRitesFactory"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the resolve-time "return target creature card from your
    /// graveyard to the battlefield; sacrifice it at the next end step"
    /// <see cref="SpellDefinition"/>. Single 1..1 target request scoped to the
    /// caster's graveyard; on resolution validates the target per CR 608.2b,
    /// returns it to the caster's battlefield, and (when
    /// <paramref name="triggers"/> is supplied) registers the delayed end-step
    /// sacrifice.
    /// </summary>
    /// <param name="caster">Spell controller — the graveyard whose creature
    /// card is returned ("your graveyard") and the destination battlefield
    /// (CR 110.2).</param>
    /// <param name="resolver">Maps the agent-supplied raw target token to the
    /// live engine object. Pass <c>o =&gt; o</c> for tests that hand cards
    /// directly.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move (and the end-step sacrifice) route through
    /// <see cref="ZoneService.MoveCard"/> so ETB / LTB triggers fire
    /// (CR 603.6a).</param>
    /// <param name="triggers">Optional. When supplied the delayed end-step
    /// sacrifice trigger is registered (CR 603.7). Shape-only callers can pass
    /// null — the reanimation still happens but the creature is not sacrificed
    /// automatically.</param>
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
                    Description: "target creature card from your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate,
                    // "your graveyard" — only the caster's graveyard is a
                    // legal source (CR 608.2b enforced again at resolution).
                    CandidateGatherer: _ => caster.Zones.Graveyard.GetCards()
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: return target creature card from your graveyard to the battlefield; sacrifice it at next end step",
                    () => Resolve(caster, chosen, resolver, zoneService, triggers)),
            });
    }

    /// <summary>
    /// Resolve the return + register the delayed sacrifice. CR 608.2b — the
    /// target must still be a creature card in the caster's graveyard;
    /// otherwise the spell does nothing.
    /// </summary>
    private static void Resolve(
        Player caster,
        ChosenSpellParams chosen,
        Func<object, object> resolver,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0) return;

        var live = resolver(chosen.Targets[0][0]);

        // CR 608.2b — illegal-on-resolution checks: must be a creature card,
        // still in the graveyard, still owned by the caster ("your graveyard").
        if (live is not Creature creature) return;
        if (creature.Zone != ZoneType.Graveyard) return;
        if (!ReferenceEquals(creature.Owner, caster)) return;

        // CR 701.20 — graveyard → battlefield under the caster's control
        // (CR 110.2). ZoneService-routed when supplied so ETB triggers fire
        // (CR 603.6a). No life loss — Footsteps has no such clause.
        Fx.ReturnFromGraveyardToBattlefield(creature, caster, zoneService);

        // "Sacrifice that creature at the beginning of the next end step."
        // CR 603.7 — one-shot delayed triggered ability. Fires on the first
        // StepStartedEvent(End) strictly after this resolve (timestamp fence
        // mirrors Goryo's Vengeance / Berserk). On fire, if the creature is
        // still on the battlefield it is sacrificed (CR 701.16). Zone-check at
        // fire time so a creature that already left the battlefield is not
        // yanked from elsewhere. Shape-only callers (triggers == null) skip
        // the cleanup.
        if (triggers == null) return;

        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var sacrificeEffect = new Effect(
            $"{CardName}: sacrifice {creature.Name} at next end step",
            () =>
            {
                if (creature.Zone != ZoneType.Battlefield) return;

                // CR 701.16 — sacrifice: owner's battlefield → owner's
                // graveyard. Bypasses indestructible (CR 702.12b).
                Fx.MoveToGraveyard(creature, ZoneMoveReason.Sacrifice);
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
