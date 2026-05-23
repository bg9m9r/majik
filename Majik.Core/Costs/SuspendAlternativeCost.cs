using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.62 — Suspend N—[cost]. Alternative cost paid from the owner's
/// hand: rather than casting the card normally, the player pays the suspend
/// cost and exiles the card with N time counters on it. At the beginning of
/// each of the owner's upkeeps, one time counter is removed; when the last
/// counter is removed the card is cast without paying its mana cost
/// (CR 702.62d). Tracking the per-upkeep tick and the "cast for free when
/// counters hit zero" payoff lives in
/// <see cref="SuspendedCardRegistry"/>; this alt cost is responsible only
/// for the initial Hand → Exile move + stamping the time counters.
///
/// Per CR 702.62b suspending a card is NOT itself casting it — calling
/// <see cref="ApplySuspend"/> directly mirrors that semantic. The alt-cost
/// shape is preserved so existing alt-cost UI (bot probes, prompts) can
/// surface "Suspend {cost}" as a candidate alongside Flashback / Plot /
/// Spectacle.
///
/// <para>Use:
/// <code>
/// var suspend = new SuspendAlternativeCost(timeCounters: 1, ManaCost.Parse("R"));
/// suspend.ApplySuspend(riftBolt, alice, registry);
/// // → riftBolt is now in alice's exile with 1 time counter; registry will
/// //   decrement on each of alice's upkeeps and auto-cast at zero.
/// </code></para>
/// </summary>
public sealed class SuspendAlternativeCost : IAlternativeCost
{
    /// <summary>Number of time counters stamped on the card when suspended
    /// (the "N" in "Suspend N—[cost]").</summary>
    public int TimeCounters { get; }

    public string Description => $"Suspend {TimeCounters}—{AlternativeManaCost}";

    /// <summary>The mana cost paid to suspend the card. CR 702.62b — this
    /// replaces the printed mana cost when suspending.</summary>
    public ManaCost AlternativeManaCost { get; }

    public SuspendAlternativeCost(int timeCounters, ManaCost suspendCost)
    {
        if (timeCounters < 0)
            throw new ArgumentOutOfRangeException(nameof(timeCounters),
                "Suspend N requires N ≥ 0 (CR 702.62a).");
        TimeCounters = timeCounters;
        AlternativeManaCost = suspendCost ?? throw new ArgumentNullException(nameof(suspendCost));
    }

    /// <summary>
    /// CR 702.62b — Suspend may only be performed on a card in the player's
    /// hand. Owner-gate matches the alt-cost contract (only the card's
    /// owner may suspend it; CR 702.62b "rather than cast a card from your
    /// hand").
    /// </summary>
    public bool CanCastFor(ICard card, Player caster) =>
        card.Zone == ZoneType.Hand && ReferenceEquals(card.Owner, caster);

    /// <summary>
    /// Suspend doesn't go through the normal cast-then-resolve pipeline,
    /// so <see cref="IAlternativeCost.OnResolved"/> is a no-op. The
    /// post-zero auto-cast path (<see cref="SuspendedCardRegistry"/>) uses
    /// the normal SpellCastFlow with the spell's printed effects and a
    /// zero mana cost — once that spell resolves it follows its normal
    /// destination (graveyard for instant/sorcery, battlefield for
    /// permanent) per CR 608.2m.
    /// </summary>
    public void OnResolved(ICard card, Player caster) { /* no cleanup; see registry */ }

    /// <summary>
    /// CR 702.62b — execute the suspend action: move the card from hand to
    /// exile and stamp <see cref="TimeCounters"/> time counters on it via
    /// the registry. Does NOT pay the mana cost — the caller is expected
    /// to have collected payment for <see cref="AlternativeManaCost"/>
    /// before invoking this method (mirrors how
    /// <see cref="SpellCastFlow"/> separates mana payment from the alt
    /// cost's mechanical side-effects).
    /// </summary>
    public void ApplySuspend(ICard card, Player caster, SuspendedCardRegistry registry)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (!CanCastFor(card, caster))
            throw new InvalidOperationException(
                $"Cannot suspend {card.Name}: must be in {caster.Name}'s hand.");

        // Hand → Exile (zone collections + zone-state pointer).
        caster.Zones.Hand.RemoveCard(card);
        caster.Zones.Exile.AddCard(card);
        card.SetZone(ZoneType.Exile);

        registry.Suspend(card, caster, TimeCounters);
    }
}
