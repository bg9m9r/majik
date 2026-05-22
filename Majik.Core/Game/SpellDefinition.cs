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
    IReadOnlyList<IAdditionalCost>? AdditionalCosts = null)
{
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
    IReadOnlyList<IAdditionalCost>? AdditionalCostPayments = null)
{
    /// <summary>
    /// Non-null view of <see cref="AdditionalCostPayments"/> — empty when
    /// no additional cost was paid for this spell. EffectFactory closures
    /// inspect this to wire effects to the cost's paid reference
    /// (e.g. <c>SacrificeCreatureCost.Sacrificed</c> for Fling-style cards).
    /// </summary>
    public IReadOnlyList<IAdditionalCost> AdditionalCostPaymentsOrEmpty =>
        AdditionalCostPayments ?? Array.Empty<IAdditionalCost>();
}
