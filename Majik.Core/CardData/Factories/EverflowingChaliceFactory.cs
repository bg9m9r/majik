using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Everflowing Chalice (Worldwake, {0}).
///
/// Artifact. Oracle text (Scryfall, verified 2026-06-02):
///   "Multikicker {2} (You may pay an additional {2} any number of times as
///    you cast this spell.)
///    This artifact enters with a charge counter on it for each time it was
///    kicked.
///    {T}: Add {C} for each charge counter on this artifact."
///
/// The canonical Multikicker (CR 702.32) scaling payoff: a free artifact
/// whose mana output is dialed in at cast time by how many times its {2}
/// kicker was paid.
///
/// ## Implementation
///
/// Card identity (Artifact, {0}) is loaded from
/// <c>Majik.Core/CardData/Cards/everflowing-chalice.json</c> through
/// <see cref="CardDefinitionFactory"/>, matching the JSON-driven card cycle
/// (same posture as <see cref="SphereOfTheSunsFactory"/>).
///
/// ## Multikicker {2} (CR 702.32 / CR 702.33)
///
/// Multikicker is wired through the generic
/// <see cref="MultikickerAdditionalCost"/> mechanism — the caller layers it
/// onto the cast via <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// <c>additionalCosts</c> list with the number of times the {2} was paid
/// (<see cref="BuildAdditionalCost(ICard, int)"/>). Paying drains 2·N mana
/// and stamps <see cref="Card.TimesKicked"/> = N on the chalice so the ETB
/// below can scale on it (CR 702.32c — "if a spell was kicked N times, …").
///
/// ## Enters WITH a charge counter for each time it was kicked (CR 614.1d / CR 702.32c / CR 122)
///
/// Modelled as a true CR 614.1d "enters the battlefield with N counters"
/// REPLACEMENT — a dynamic-count <see cref="EntersWithCountersReplacement"/>
/// keyed on <see cref="Card.TimesKicked"/>, registered against the supplied
/// <see cref="ReplacementBus"/>. When the chalice's Stack → Battlefield move
/// runs through <see cref="Services.ZoneService"/>, the replacement queues
/// that many <see cref="CounterType.Charge"/> counters onto the
/// <see cref="ZoneMoveIntent.CountersOnEnter"/> bag so the artifact enters
/// WITH the counters already on it (CR 122 / CR 614.1d) — never a window
/// where it sits on the battlefield with zero, and observable by other
/// ETB-counter replacements. This is the correctness upgrade from the older
/// ETB-trigger shape (which placed the counters in a separate event after the
/// artifact had already entered).
///
/// A multikicker paid zero times = zero charge counters (the chalice still
/// enters, but taps for nothing). The dynamic count is consumed on entry —
/// <see cref="ZoneService"/> later clears the kicker sentinels (CR 400.7) so a
/// blink / token copy enters with zero.
///
/// ## {T}: Add {C} for each charge counter on this artifact (CR 605.1)
///
/// A single dynamic <see cref="Abilities.ManaAbility"/> whose
/// <c>Func&lt;ManaCost&gt;</c> generator counts the chalice's charge counters
/// at activation and produces that many colourless mana (CR 107.4c — {C}
/// folds into the generic bucket). Mana abilities don't use the stack
/// (CR 605.3b). Gated on the chalice being on the battlefield and untapped
/// (the printed {T}).
/// </summary>
[CardName("Everflowing Chalice")]
public static class EverflowingChaliceFactory
{
    public const string CardName = "Everflowing Chalice";
    public const string PrintedManaCost = "{0}";
    public const string MultikickerCostText = "{2}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("everflowing-chalice");

    /// <summary>
    /// CR 702.32 — the per-kick Multikicker cost ({2}). Exposed so callers
    /// (bot decision layer, UI, scripted tests) can build the additional cost
    /// without hard-coding the value.
    /// </summary>
    public static ManaCost MultikickerCost => ManaCost.Parse(MultikickerCostText);

    /// <summary>
    /// Construct Everflowing Chalice with no live replacement wiring. The
    /// "enters with a charge counter for each time kicked" replacement is NOT
    /// registered (no bus supplied); the dynamic mana ability is attached.
    /// Suitable for shape / <see cref="NamedCardFactory"/> dispatch tests.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Everflowing Chalice. When <paramref name="replacements"/> is
    /// supplied, the CR 614.1d "enters with a charge counter for each time it
    /// was kicked" replacement is registered so a routed
    /// <see cref="Services.ZoneService"/> ETB makes the artifact enter WITH the
    /// charge counters.
    /// </summary>
    public static Artifact Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var chalice = (Artifact)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // ETB — "This artifact enters with a charge counter on it for each
        // time it was kicked." (CR 614.1d / CR 702.32c / CR 122.) Modelled
        // as a true "enters the battlefield with N counters" REPLACEMENT
        // (EntersWithCountersReplacement) with a dynamic count keyed on the
        // cast-time multikicker tally (Card.TimesKicked). The ZoneService ETB
        // pipeline queues the charge counters onto the move intent so the
        // artifact enters WITH them already present.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<ZoneMoveIntent>(
                new EntersWithCountersReplacement(
                    chalice, CounterType.Charge, () => chalice.TimesKicked));
        }

        // ----------------------------------------------------------------
        // {T}: Add {C} for each charge counter on this artifact. (CR 605.1 —
        // mana ability; CR 605.3b — doesn't use the stack.)
        //
        // Dynamic Func<ManaCost> generator: count the charge counters at
        // activation, produce that many colourless mana. {C}×N folds into
        // the generic bucket (CR 107.4c). The standard tap-as-cost overload
        // is used (the printed cost is {T}). Gated on the chalice being on
        // the battlefield and untapped.
        // ----------------------------------------------------------------
        chalice.AddAbility(new Abilities.ManaAbility(
            source: chalice,
            controller: owner,
            manaGenerator: () => ChargeCounterMana(chalice),
            canActivateCheck: () => chalice.Zone == ZoneType.Battlefield
                                    && !chalice.IsTapped));

        return chalice;
    }

    /// <summary>
    /// CR 605.1 — the chalice's tap output: one colourless mana per charge
    /// counter currently on it. Folds into the generic bucket via
    /// <see cref="ManaCost"/> (CR 107.4c). Zero counters = no mana.
    /// </summary>
    private static ManaCost ChargeCounterMana(Artifact chalice)
    {
        var n = chalice.Counters.Count(CounterType.Charge);
        return n <= 0 ? ManaCost.Zero : ManaCost.Parse(new string('C', n));
    }

    /// <summary>
    /// Construct a <see cref="MultikickerAdditionalCost"/> for the chalice
    /// paid <paramref name="times"/> times. Convenience builder for callers
    /// that have decided how many times to pay the {2} multikicker — they
    /// pass it through <see cref="Majik.Core.Game.SpellCastFlow"/>'s
    /// <c>additionalCosts</c>. <paramref name="times"/> of 0 = cast without
    /// kicking.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card, int times) =>
        new MultikickerAdditionalCost(card, MultikickerCost, times);
}
