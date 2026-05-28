using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Otherworldly Journey (Champions of Kamigawa,
/// {1}{W}).
///
/// Instant. Oracle text:
///   "Exile target creature. At the beginning of the next end step,
///    return that card to the battlefield under its owner's control
///    with a +1/+1 counter on it."
///
/// CR 701.21 (Exile) + CR 603.7 (delayed triggered abilities) + CR 122
/// (counters) — Otherworldly Journey is the "delayed return + permanent
/// pump" flicker. Same delayed-end-step shape as
/// <see cref="TouchTheSpiritRealmFactory"/>'s Channel rider, but the
/// return mints a +1/+1 counter (CR 122.1c) on re-entry. The cast body
/// targets ANY creature (own or opponent's) — distinct from Cloudshift /
/// Ephemerate / Restoration Angel, which all restrict to "you control".
/// Casting on an opponent's creature is a save-from-removal play; in
/// practice the spell is almost always cast on your own creature to
/// dodge removal + leave the +1/+1 counter behind.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{W}.
/// - <b>Cast body</b> — <see cref="BuildSpellDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target creature"
///   <see cref="TargetRequest"/> sourced from a live <c>CandidateGatherer</c>
///   walking every player's battlefield for <see cref="Creature"/>
///   permanents. <see cref="BotIntent.Protection"/> — the dominant use
///   is dodging removal on your own creature (bot ranker tilts toward
///   controller-side creatures via the standard Protection heuristic).
/// - <b>Resolve</b>: re-checks the target is still a battlefield
///   Creature (CR 608.2b — illegal target → no effect). Exile via
///   <see cref="ZoneService"/> when supplied so
///   <see cref="CardMovedEvent"/> fires; falls back to owner-routed
///   zone mutation.
/// - <b>Delayed end-step return</b> (CR 603.7): when a
///   <see cref="TriggerManager"/> is supplied, registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> that fires on the first
///   <see cref="StepStartedEvent"/> with <c>StepType == End</c> and
///   <c>Timestamp &gt; resolvedAt</c> (same activation-time fence as
///   Touch the Spirit Realm / Yorion / Wrenn's Resolve). On resolve:
///   re-check the still-exiled card (CR 111.8 token guard), return via
///   <see cref="ZoneService"/> when supplied so ETB triggers fire,
///   set controller to the card's OWNER (CR 614 — "under its owner's
///   control"), then stamp a single +1/+1 counter via
///   <see cref="CountersService.Add"/> (CR 122.1c — routed through the
///   <see cref="ReplacementBus"/> when supplied so Hardened Scales /
///   Doubling Season replacements can rewrite the count).
///
/// ## Deferred (v1 gaps)
/// - <b>"With a +1/+1 counter as it enters"</b> (CR 614.1c — entering
///   replacement effects): the printed text reads "return … with a
///   +1/+1 counter on it" — a single combined event. v1 sequences this
///   as (a) zone-move to battlefield, then (b) counter placement after
///   the move event resolves. This means ETBs that observe the counter
///   at re-entry (Heliod's Pilgrim style "if it enters with a counter")
///   could see the creature without the counter, then with it.
///   Acceptable for v1 — no Modern card observes this granularity for
///   Otherworldly Journey specifically. Tracked alongside Yorion /
///   Conjurer's Closet "return with effect" as a shared "entering with
///   modifications" primitive.
/// - <b>Shape-only fallback</b>: without a <see cref="TriggerManager"/>,
///   the cast body still exiles the target but the delayed return is
///   skipped (matches Touch the Spirit Realm / Yorion / Wrenn's
///   Resolve two-mode posture). Structural / dispatch tests use this
///   path; end-to-end tests supply the full trigger manager.
/// </summary>
[CardName("Otherworldly Journey")]
public static class OtherworldlyJourneyFactory
{
    public const string CardName = "Otherworldly Journey";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>Construct Otherworldly Journey as an Instant owned and
    /// controlled by <paramref name="owner"/>. Card shape only — the
    /// resolve closure is produced by <see cref="BuildSpellDefinition"/>.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Otherworldly Journey.
    /// Single 1..1 "target creature" request (any controller); on
    /// resolve, exile via <paramref name="zones"/> (or owner-routed
    /// fallback) and register a delayed end-step return + counter via
    /// <paramref name="triggers"/> + <paramref name="replacements"/>.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        TriggerManager? triggers = null,
        ZoneService? zones = null,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection,
                    // CR 608.2b — gather any battlefield Creature. Both
                    // controllers' creatures qualify; the bot ranker's
                    // Protection intent prefers controller-side picks.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    return Array.Empty<IEffect>();
                }
                if (chosen.Targets[0][0] is not Creature target)
                {
                    return Array.Empty<IEffect>();
                }

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target creature; return at next end step with +1/+1 counter",
                        () => ResolveCast(target, caster, triggers, zones, replacements)),
                };
            });
    }

    // --- Resolve: exile + register delayed return (CR 701.21 / 603.7) ----
    private static void ResolveCast(
        Creature target,
        Player caster,
        TriggerManager? triggers,
        ZoneService? zones,
        ReplacementBus? replacements)
    {
        // CR 608.2b — resolution-time legality re-check.
        if (target.Zone != ZoneType.Battlefield) return;

        ExileTarget(target, caster, zones);

        // CR 603.7 — delayed end-step return. Only register when a
        // TriggerManager is supplied (shape-only fallback per Touch the
        // Spirit Realm / Yorion / Wrenn's Resolve).
        if (triggers == null) return;

        RegisterDelayedReturn(target, caster, triggers, zones, replacements);
    }

    private static void ExileTarget(Creature target, Player caster, ZoneService? zones)
    {
        // CR 701.21 — prefer ZoneService so CardMovedEvent fires.
        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Battlefield, ZoneType.Exile);
            return;
        }
        var fromOwner = target.Owner ?? caster;
        fromOwner.Zones.Battlefield.RemoveCard(target);
        fromOwner.Zones.Exile.AddCard(target);
        target.SetZone(ZoneType.Exile);
    }

    private static void RegisterDelayedReturn(
        Creature target,
        Player caster,
        TriggerManager triggers,
        ZoneService? zones,
        ReplacementBus? replacements)
    {
        var resolvedAt = DateTime.UtcNow;
        var returnEffect = new Effect(
            $"{CardName} — return exiled creature at next end step with +1/+1 counter (CR 603.7 / CR 614 / CR 122.1c)",
            () => ResolveDelayedReturn(target, caster, zones, replacements));

        var delayed = new DelayedTriggeredAbility(
            source: target,
            controller: caster,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End && e.Timestamp > resolvedAt),
            effects: new IEffect[] { returnEffect });

        triggers.RegisterDelayed(delayed);
    }

    private static void ResolveDelayedReturn(
        Creature target,
        Player caster,
        ZoneService? zones,
        ReplacementBus? replacements)
    {
        // CR 111.8 — token guard. CR 614 — "under its owner's control".
        if (target.Zone != ZoneType.Exile) return;

        var returnOwner = target.Owner ?? caster;

        if (zones != null)
        {
            zones.MoveCard(target, ZoneType.Exile, ZoneType.Battlefield, returnOwner);
        }
        else
        {
            returnOwner.Zones.Exile.RemoveCard(target);
            returnOwner.Zones.Battlefield.AddCard(target);
            target.SetZone(ZoneType.Battlefield);
            target.SetController(returnOwner);
        }

        // CR 122.1c — +1/+1 counter on returned card.
        if (target.Zone == ZoneType.Battlefield)
        {
            CountersService.Add(target, CounterType.PlusOnePlusOne, 1, replacements);
        }
    }
}
