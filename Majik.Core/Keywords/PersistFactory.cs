using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.79 — Persist: "When this creature dies, if it had no -1/-1 counters
/// on it, return it to the battlefield under its owner's control with a -1/-1
/// counter on it."
///
/// Persist is the negative-counter mirror of Undying (CR 702.93). This
/// primitive was promoted out of <see cref="Majik.Core.CardData.Factories.KitchenFinksFactory"/>
/// once a second + third Persist card landed (Murderous Redcap, Glen Elendra
/// Archmage) — same shape as the Modular promotion (see
/// <see cref="ModularFactory"/>).
///
/// Two pieces of wiring are produced by <see cref="Build"/>:
///
/// 1. A <see cref="KeywordAbility"/> marker (<c>"Persist"</c>) so card
///    inspectors / tooltips / Layer-system scanners can see the keyword.
///    Mirrors the Modular / Undying / Flying marker convention.
///
/// 2. A <see cref="TriggeredAbility"/> over <see cref="Triggers.OnDies"/>
///    (Battlefield → Graveyard self) that:
///      - InterveningIf (CR 603.4): no <see cref="CounterType.MinusOneMinusOne"/>
///        counters at trigger-resolution time. The counter bag survives the
///        zone move (Undying-shape — see <see cref="UndyingFactory"/>), so
///        this reflects the state at death.
///      - On resolution moves the creature from Graveyard → Battlefield via
///        a raw zone-move (so subsequent flicker / Persist chains don't
///        re-fire the death trigger), clears the counter bag (CR 121.2 —
///        counters leave when a permanent changes zones), then adds exactly
///        one -1/-1 counter via <see cref="CountersService.Add"/> so any
///        replacement bus rewrites (none today; placeholder symmetry with
///        Undying / Modular) apply.
///
/// <c>activeZones</c> is <c>{Battlefield, Graveyard}</c> so the trigger
/// survives the death zone-move (ZoneService sets the card's zone before
/// publishing the event, so a Battlefield-only ActiveZones would not match
/// at evaluation time).
/// </summary>
public static class PersistFactory
{
    /// <summary>Construct a Persist trigger with no replacement-bus routing
    /// for the -1/-1 counter add. Equivalent to <see cref="Build(Creature, ReplacementBus?)"/>
    /// with <c>replacements: null</c>.</summary>
    public static TriggeredAbility Build(Creature source) =>
        Build(source, replacements: null);

    /// <summary>
    /// Build the Persist triggered ability for <paramref name="source"/>.
    /// The "with a -1/-1 counter on it" return-side placement routes through
    /// the supplied <see cref="ReplacementBus"/> via
    /// <see cref="CountersService.Add"/> — same posture as
    /// <see cref="UndyingFactory.Build(Creature, ReplacementBus?)"/>. When
    /// <paramref name="replacements"/> is null the counter is placed directly
    /// (matches today's untouched callers).
    /// </summary>
    public static TriggeredAbility Build(Creature source, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(source);

        var owner = source.Owner
            ?? throw new InvalidOperationException("Persist source must have an owner.");
        var controller = source.Controller ?? owner;

        // ----------------------------------------------------------------
        // 1. Keyword marker — reminder-text shape ("Persist") so card
        //    inspectors / tooltips / future Layer scanners can see the
        //    keyword. Value-only; the trigger does the actual work.
        // ----------------------------------------------------------------
        source.AddAbility(new KeywordAbility("Persist", source, controller));

        // ----------------------------------------------------------------
        // 2. Death trigger (CR 702.79). Mirrors UndyingFactory with the
        //    counter polarity flipped (+1/+1 → -1/-1).
        // ----------------------------------------------------------------
        var effect = new Effect("Persist — return to battlefield with -1/-1 counter", () =>
        {
            // Guard: creature must still be in graveyard (replacement
            // effects could have moved it elsewhere — unusual but legal).
            if (source.Zone != ZoneType.Graveyard) return;

            var cardOwner = source.Owner;
            if (cardOwner == null) return;

            // Move from graveyard to battlefield (CR 702.79b). Raw zone-move
            // mirrors UndyingFactory — does NOT republish a CardMovedEvent
            // (so we don't auto-fire other ETB triggers from inside the
            // Persist effect). Callers that want full ETB-routed return
            // should swap to ZoneService.MoveCard at that layer.
            cardOwner.Zones.Graveyard.RemoveCard(source);
            cardOwner.Zones.Battlefield.AddCard(source);
            source.SetZone(ZoneType.Battlefield);
            source.SetController(cardOwner);

            // CR 121.2 — counters left the battlefield when the creature
            // died. Clear the bag so the second death after a Persist
            // return accurately reflects the new (now-counter-bearing)
            // state and the interveningIf correctly suppresses re-trigger.
            foreach (var entry in source.Counters.All.ToList())
            {
                source.Counters.Remove(entry.Key, entry.Value);
            }

            // Persist grant: one -1/-1 counter (CR 702.79b). Routed via
            // CountersService for replacement-bus symmetry with Undying /
            // Modular (Hardened-Scales-equivalent infra placeholder).
            CountersService.Add(source, CounterType.MinusOneMinusOne, 1, replacements);

            // Permanent ETB bookkeeping — re-stamp summoning-sickness /
            // entry timestamp (same as UndyingFactory).
            source.MarkEnteredBattlefield();
        });

        var deathTrigger = new TriggeredAbility(
            source: source,
            controller: controller,
            condition: Triggers.OnDies(source),
            effects: new IEffect[] { effect },
            interveningIf: () => source.Counters.Count(CounterType.MinusOneMinusOne) == 0,
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        source.AddAbility(deathTrigger);
        return deathTrigger;
    }
}
