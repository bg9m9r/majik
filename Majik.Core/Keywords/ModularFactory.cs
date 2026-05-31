using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.43 — Modular N. Promoted to a shared primitive from
/// <see cref="Majik.Core.CardData.Factories.ArcboundRavagerFactory"/> after a
/// second + third Modular card (Arcbound Worker, Arcbound Stinger) joined the
/// roadmap.
///
/// Three pieces of wiring are produced by <see cref="Build"/>:
///
/// 1. A <see cref="KeywordAbility"/> marker (e.g. <c>"Modular 1"</c>) so card
///    inspectors / tooltips / future Layer-system scanners can see the keyword.
///    The numeric N is embedded in the keyword string (same convention as
///    reminder-text-style markers — mirrors the existing "First strike",
///    "Lifelink" patterns). The ability is value-only; counters are wired via
///    the two effects below.
///
/// 2. CR 702.43a / CR 614.1d — "this creature enters the battlefield with N
///    +1/+1 counters on it." Registered against the supplied
///    <see cref="ReplacementBus"/> via <see cref="EntersWithCountersReplacement"/>
///    so the ZoneService's ETB pipeline reads the rewritten
///    <see cref="ZoneMoveIntent.PlusOneCountersOnEnter"/> and routes through
///    <see cref="CountersService.Add"/> on landing. Hardened Scales /
///    Doubling Season bumps therefore apply (PR #494).
///    When <paramref name="replacements"/> is null no replacement is
///    registered; callers can stamp the counters manually via
///    <see cref="MarkEntersWithCounters"/> (shape-only-test fallback —
///    mirrors the Arcbound Ravager v1 posture).
///
/// 3. CR 702.43b — "When this creature dies, you may put a +1/+1 counter on
///    target artifact creature for each +1/+1 counter on this creature."
///    Implemented as a <see cref="TriggeredAbility"/> firing on the source's
///    Battlefield → Graveyard transition (via <see cref="Triggers.OnDies"/>).
///    The counter total is snapshot-read off the dying permanent's
///    <see cref="Permanent.Counters"/> bag at resolution time — the bag is
///    NOT cleared on zone-move (Undying-shape), so the death-side count
///    accurately reflects what the creature had when it left the battlefield.
///    Bestowal target is picked deterministically (v1) — first artifact
///    creature on the controller's battlefield other than the source.
///    The "you may" rider consults <see cref="IPlayerAgent.ChooseYesNoAsync"/>
///    with <see cref="BotIntent.CardAdvantage"/>; agent-less callers
///    auto-accept (legacy posture). When the bag total is 0 OR no legal
///    target exists, the trigger resolves as a no-op.
///
/// Activated abilities like Arcbound Ravager's "Sacrifice an artifact: +1/+1
/// counter" are NOT part of Modular — those stay on the per-card factory.
/// </summary>
public static class ModularFactory
{
    /// <summary>
    /// Wire Modular N on <paramref name="source"/>: attach the keyword marker,
    /// register the ETB +1/+1-counter replacement against
    /// <paramref name="replacements"/>, and register the death-trigger against
    /// <paramref name="triggers"/>. Returns the created
    /// <see cref="TriggeredAbility"/> so callers can introspect or further
    /// configure it (e.g. attach additional intervening-if checks).
    /// </summary>
    /// <param name="source">The Modular permanent (must be an artifact
    /// creature per CR 702.43a — not enforced here; the per-card factory is
    /// responsible for the printed type line).</param>
    /// <param name="n">Modular N — the printed value.</param>
    /// <param name="effects">Optional ContinuousEffectsService; reserved for
    /// future Modular-layer wiring (e.g. Modular tokens, replacement-aware
    /// pump). Currently unused — the death body reads the counter bag
    /// directly. Accepting the param now matches the Undying-family signature
    /// shape and avoids a churn rev later.</param>
    /// <param name="replacements">ReplacementBus to register the ETB
    /// +1/+1-counter replacement against (CR 614.1d). May be null — no
    /// replacement is registered; callers can stamp counters manually via
    /// <see cref="MarkEntersWithCounters"/>.</param>
    /// <param name="triggers">TriggerManager for the death trigger. May be
    /// null — the trigger is still attached to the card shape so dispatcher
    /// / structural tests can observe it.</param>
    /// <param name="agent">Optional IPlayerAgent for the "you may" prompt
    /// (CR 117.x / 605.1). When null, the may-rider auto-accepts (legacy
    /// posture used by every factory before ChooseYesNoAsync shipped).</param>
    public static TriggeredAbility Build(
        Permanent source,
        int n,
        ContinuousEffectsService? effects,
        ReplacementBus? replacements,
        TriggerManager? triggers,
        IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), "Modular N must be non-negative.");

        var owner = source.Owner
            ?? throw new InvalidOperationException("Modular source must have an owner.");
        var controller = source.Controller ?? owner;

        // ----------------------------------------------------------------
        // 1. Keyword marker — reminder-text shape ("Modular 1" / "Modular 2"
        //    / etc.) so card inspectors / tooltips / future Layer scanners
        //    can see the keyword. Value-only; counters are wired below.
        // ----------------------------------------------------------------
        source.AddAbility(new KeywordAbility($"Modular {n}", source, controller));

        // ----------------------------------------------------------------
        // 2. ETB +N +1/+1 counters (CR 702.43a / CR 614.1d).
        //    Routed through ReplacementBus -> ZoneService -> CountersService
        //    so Hardened Scales etc. bumps apply (PR #494).
        // ----------------------------------------------------------------
        if (replacements != null && n > 0)
        {
            replacements.Register<ZoneMoveIntent>(
                new EntersWithCountersReplacement(source, n));
        }

        // ----------------------------------------------------------------
        // 3. Death trigger (CR 702.43b). Snapshot the counter total BEFORE
        //    the move semantics — the bag's value at trigger-resolution time
        //    survives the zone-move (Undying-shape — counters live on the
        //    card object until cleared on next ETB), so we can read it here.
        //    Resolution: ChooseYesNoAsync(CardAdvantage) -> pick first
        //    artifact-creature target on controller's battlefield -> remove
        //    counters from the graveyard object -> CountersService.Add onto
        //    the chosen target (so Hardened Scales bumps the bestowal too).
        // ----------------------------------------------------------------
        var deathEffect = new Effect(
            $"Modular {n}: move +1/+1 counters to target artifact creature",
            async ctx =>
            {
                var counters = source.Counters.Count(CounterType.PlusOnePlusOne);
                if (counters <= 0) return;

                var target = FindArtifactCreatureTarget(controller, source);
                if (target == null) return;

                // "You may" — CR 117.x / 605.1. Default agent posture is
                // accept on CardAdvantage; null-agent path auto-accepts to
                // preserve pre-prompt behaviour.
                if (agent != null)
                {
                    var yes = (await agent.ChooseYesNoAsync(
                        "Move +1/+1 counters to target artifact creature?",
                        BotIntent.CardAdvantage).ConfigureAwait(false));
                    if (!yes) return;
                }

                // CR 121.2 — counters left the battlefield when the source
                // died, but they're still recorded on the card object so we
                // can read the count. Remove them from the graveyard object
                // (so a subsequent flicker / Undying return doesn't
                // double-stamp) and add them to the chosen target (routed
                // through CountersService so Hardened Scales bumps the
                // bestowal — PR #494).
                source.Counters.Remove(CounterType.PlusOnePlusOne, counters);
                CountersService.Add(target, CounterType.PlusOnePlusOne, counters, replacements);
            });

        var deathTrigger = new TriggeredAbility(
            source: source,
            controller: controller,
            condition: Triggers.OnDies(source),
            effects: new IEffect[] { deathEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        source.AddAbility(deathTrigger);
        triggers?.RegisterTriggeredAbility(deathTrigger);

        return deathTrigger;
    }

    /// <summary>
    /// Shape-only fallback for tests that put a Modular creature on the
    /// battlefield without funnelling through <see cref="Services.ZoneService"/>
    /// + <see cref="ReplacementBus"/>. Manually stamps N +1/+1 counters
    /// (CR 702.43a) on <paramref name="card"/>. Idempotent per call; callers
    /// should invoke at ETB time exactly once.
    /// </summary>
    public static void MarkEntersWithCounters(Permanent card, int n)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (n <= 0) return;
        card.Counters.Add(CounterType.PlusOnePlusOne, n);
    }

    /// <summary>
    /// Find a legal Modular bestowal target — an artifact creature on
    /// <paramref name="controller"/>'s battlefield, excluding
    /// <paramref name="self"/>. v1 deterministic — returns the first match.
    /// CR 702.43b's "target artifact creature" is not controller-restricted;
    /// opponent-side scans are deferred until the engine exposes a cross-
    /// battlefield enumerator (no <c>Player.Opponents</c> in v1 — the common
    /// case is an Affinity / Hardened Scales deck packed with the
    /// controller's own artifact creatures). Promotion to a full
    /// <see cref="TargetRequest"/> prompt is the next step.
    /// </summary>
    private static Creature? FindArtifactCreatureTarget(Player controller, Permanent self) =>
        controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, self) && c.HasType(CardType.Artifact))
            .FirstOrDefault();
}
