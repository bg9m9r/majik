using Majik.Core.Abilities;
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
public sealed record SpellDefinition(
    IReadOnlyList<string> Modes,
    bool HasVariableX,
    IReadOnlyList<TargetRequest> TargetRequests,
    Func<ChosenSpellParams, IReadOnlyList<IEffect>> EffectFactory)
{
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
    IReadOnlyList<int>? ModeIndexes = null);
