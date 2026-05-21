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
public sealed record ChosenSpellParams(
    int? ModeIndex,
    int? X,
    IReadOnlyList<IReadOnlyList<object>> Targets,
    ManaPayment Mana,
    IReadOnlyList<Player>? AllPlayers = null);
