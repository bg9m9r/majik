using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.Game;

/// <summary>
/// Per-card metadata that <see cref="SpellCastFlow"/> uses to prompt the
/// caster correctly: any mode choices, X cost, and target requests.
/// The <see cref="EffectFactory"/> receives the chosen parameters and
/// builds the actual effects executed on resolution.
///
/// Produced at cast time by the data-driven oracle binder
/// (<c>OracleSpellBinder</c>); test code may also build one directly.
/// </summary>
/// <remarks>
/// <para><see cref="ModeIntents"/> is parallel to <see cref="Modes"/>:
/// when populated, <c>ModeIntents[i]</c> is the
/// <see cref="Majik.Core.Cards.BotIntent"/> of the matching mode clause.
/// Empty for non-modal spells, or when the binder produced modes from a
/// template path that doesn't (yet) classify per-clause intent. The bot
/// falls back to legacy label scoring in that case.</para>
/// </remarks>
public sealed record SpellDefinition(
    IReadOnlyList<string> Modes,
    bool HasVariableX,
    IReadOnlyList<TargetRequest> TargetRequests,
    Func<ChosenSpellParams, IReadOnlyList<IEffect>> EffectFactory,
    IReadOnlyList<BotIntent>? ModeIntents = null,
    IReadOnlyList<IAdditionalCost>? AdditionalCosts = null,
    int MinModes = 1,
    int MaxModes = 1,
    EscalateSpec? Escalate = null,
    DamageDivisionSpec? DamageDivision = null)
{
    /// <summary>
    /// CR 700.2d / CR 700.2e — true when the caster may pick MORE than one
    /// mode ("Choose one or more", "Choose two", "Choose two or three").
    /// Single-mode ("Choose one") spells leave <see cref="MinModes"/> /
    /// <see cref="MaxModes"/> at their (1, 1) default and route through the
    /// legacy scalar <c>ChooseModeAsync</c> path; multi-mode spells route
    /// through <c>ChooseModesAsync</c> and populate
    /// <see cref="ChosenSpellParams.ModeIndexes"/>.
    /// </summary>
    public bool IsMultiMode => Modes.Count > 0 && MaxModes > 1;

    /// <summary>
    /// Non-null view of <see cref="ModeIntents"/> — empty when no per-mode
    /// intents have been computed. Consumers should prefer this accessor
    /// so they don't need null-checks at every read site.
    /// </summary>
    public IReadOnlyList<BotIntent> ModeIntentsOrEmpty =>
        ModeIntents ?? Array.Empty<BotIntent>();

    /// <summary>
    /// Non-null view of <see cref="AdditionalCosts"/> — empty when the
    /// card carries no spell-intrinsic additional costs. CR 601.2f.
    /// <see cref="SpellCastFlow"/> merges these with any caller-supplied
    /// additional costs at cast time.
    /// </summary>
    public IReadOnlyList<IAdditionalCost> AdditionalCostsOrEmpty =>
        AdditionalCosts ?? Array.Empty<IAdditionalCost>();

    public static SpellDefinition Vanilla(
        Func<ChosenSpellParams, IReadOnlyList<IEffect>> effectFactory) =>
        new(Array.Empty<string>(), false, Array.Empty<TargetRequest>(), effectFactory);
}

/// <summary>What the caster chose during the cast flow.</summary>
/// <remarks>
/// <see cref="ModeIndex"/> is the legacy single-mode pick (set by Choose-one
/// modal spells). <see cref="ModeIndexes"/> is the multi-mode list (set by
/// Choose-two / Choose-one-or-both / Choose-one-or-more spells). When both
/// are non-null, multi-mode consumers should prefer the list; legacy
/// consumers that only read <see cref="ModeIndex"/> still see the first
/// chosen mode (the cast flow keeps the scalar field in sync with the
/// first list entry).
/// </remarks>
public sealed record ChosenSpellParams(
    int? ModeIndex,
    int? X,
    IReadOnlyList<IReadOnlyList<object>> Targets,
    ManaPayment Mana,
    IReadOnlyList<Player>? AllPlayers = null,
    IReadOnlyList<int>? ModeIndexes = null,
    IReadOnlyList<IAdditionalCost>? AdditionalCostPayments = null,
    IReadOnlyList<DamageAllocation>? DamageDivision = null)
{
    /// <summary>
    /// Non-null view of <see cref="DamageDivision"/> — empty when the spell
    /// declared no <see cref="SpellDefinition.DamageDivision"/>. CR 601.2d:
    /// the caster's chosen split of the printed damage across the chosen
    /// targets, recorded at cast time alongside the targets. Each entry pairs
    /// a chosen target token (index into the divided target slot) with the
    /// amount of damage assigned to it. EffectFactory closures read this
    /// instead of an even-split fallback so the dealt amounts honour the
    /// caster's announced division.
    /// </summary>
    public IReadOnlyList<DamageAllocation> DamageDivisionOrEmpty =>
        DamageDivision ?? Array.Empty<DamageAllocation>();

    /// <summary>
    /// Non-null view of <see cref="AdditionalCostPayments"/> — empty when
    /// no additional cost was paid for this spell. EffectFactory closures
    /// inspect this to wire effects to the cost's paid reference
    /// (e.g. <c>SacrificeCreatureCost.Sacrificed</c> for Fling-style cards).
    /// </summary>
    public IReadOnlyList<IAdditionalCost> AdditionalCostPaymentsOrEmpty =>
        AdditionalCostPayments ?? Array.Empty<IAdditionalCost>();
}

/// <summary>
/// CR 601.2d / CR 119.4 — declares that a spell deals a fixed total amount of
/// damage that the caster DIVIDES at cast time among the targets it chose for
/// one of its target-request slots ("~ deals N damage divided as you choose
/// among one, two, or three targets"). <see cref="SpellCastFlow"/> spots this
/// on the bound <see cref="SpellDefinition"/>, prompts the caster's agent at
/// the CR 601.2d announcement point (right after target collection,
/// CR 601.2c), and records the chosen split on
/// <see cref="ChosenSpellParams.DamageDivision"/>.
/// </summary>
/// <param name="TotalDamage">CR 119.4 — the printed damage that MUST be
/// divided so each chosen target gets at least 1 and the assignments sum to
/// exactly this value.</param>
/// <param name="TargetSlotIndex">Index into
/// <see cref="ChosenSpellParams.Targets"/> identifying which target-request
/// slot holds the recipients the damage is divided among (almost always 0 —
/// the single divided-damage request).</param>
public sealed record DamageDivisionSpec(
    int TotalDamage,
    int TargetSlotIndex = 0);

/// <summary>
/// CR 601.2d — one entry of a damage division: the chosen target the caster
/// assigned <see cref="Amount"/> damage to. <see cref="TargetSlotPosition"/>
/// is the index of the target within the divided target slot
/// (<see cref="SpellDefinition.DamageDivision"/>'s
/// <see cref="DamageDivisionSpec.TargetSlotIndex"/>), so a resolution-time
/// EffectFactory can correlate the amount with the live (re-resolved) target
/// even after illegal-at-resolution targets are filtered (CR 608.2b). The raw
/// chosen <see cref="Target"/> token is also carried for closures that resolve
/// it directly.
/// </summary>
public sealed record DamageAllocation(
    object Target,
    int TargetSlotPosition,
    int Amount);

/// <summary>
/// CR 702.121 — Escalate. The additional cost a modal ("choose one or more")
/// spell must pay for EACH mode chosen beyond the first (CR 702.121a). The
/// caster pays this cost (modesChosen − 1) times as the spell is cast
/// (CR 601.2f — as an additional cost), so choosing two modes costs the
/// escalate cost once, three modes costs it twice, and so on.
///
/// <para>
/// <see cref="BuildPerModeCost"/> is invoked by <see cref="SpellCastFlow"/>
/// once per extra mode to produce a fresh <see cref="IAdditionalCost"/>
/// instance (e.g. a new <c>DiscardACardAdditionalCost</c> for Collective
/// Brutality's "Escalate—Discard a card"). Each instance is pre-checked for
/// affordability and paid in order; if any extra mode's cost can't be paid
/// the cast is illegal (CR 601.2g — no partial payment of additional costs),
/// surfaced as the same <see cref="InvalidOperationException"/> the rest of
/// the additional-cost machinery throws.
/// </para>
///
/// <para>
/// Modelling escalate as a per-extra-mode cost FACTORY (rather than a single
/// cost paid N times) keeps each payment independent — the discard picker can
/// nominate a different card per extra mode, and the paid references are
/// surfaced individually on <see cref="ChosenSpellParams.AdditionalCostPayments"/>.
/// </para>
/// </summary>
public sealed record EscalateSpec(
    string Description,
    Func<ICard, IAdditionalCost> BuildPerModeCost,
    Func<Player, int, bool>? CanPayExtra = null)
{
    /// <summary>
    /// CR 601.2g — aggregate affordability for paying the escalate cost
    /// <paramref name="extraModes"/> times up front (before any payment
    /// mutates state). Defaults to "true" when no probe is supplied — the
    /// per-payment loop in <see cref="SpellCastFlow"/> then catches a
    /// mid-sequence shortfall. Cards whose escalate cost depletes a countable
    /// resource (discard-a-card → hand size; pay N life → life total) supply a
    /// real probe so a "chose 3 modes with 2 cards in hand" cast is rejected
    /// atomically rather than discarding one card and then aborting.
    /// </summary>
    public bool CanPayExtraModes(Player caster, int extraModes) =>
        extraModes <= 0 || (CanPayExtra?.Invoke(caster, extraModes) ?? true);
}
