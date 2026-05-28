namespace Majik.Core.Game;

/// <summary>
/// Engine-facing read-only view of a player's auto-pass preferences.
/// Defined in <see cref="Majik.Core.Game"/> so
/// <see cref="PriorityLoop"/> can consult prefs without depending on
/// the upper <c>Majik.Core.Api</c> layer (where the wire DTO
/// <c>AutoPassPrefs</c> lives and implements this interface).
///
/// <para>The shape mirrors the portal's <c>AutoPassDeps</c>:
/// FullControl (suppress auto-pass entirely) + PhaseStops (per-side
/// stops keyed by wire phase label). Kept narrow so future fields on
/// the wire DTO that aren't engine-relevant (e.g. UI-only toggles)
/// don't bleed into the engine's auto-pass gate.</para>
/// </summary>
public interface IAutoPassPrefsView
{
    /// <summary>When <c>true</c>, suppress all server-side auto-pass.
    /// The human is holding the Full Control modifier and wants every
    /// priority window to surface a prompt.</summary>
    bool FullControl { get; }

    /// <summary>Per-phase stop map keyed by wire phase label
    /// (<c>"Untap"</c>, <c>"Upkeep"</c>, <c>"PreCombatMain"</c>, …).
    /// Values are <c>"mine"</c> or <c>"theirs"</c> — the side the stop
    /// fires on. Missing key = no stop for that phase.</summary>
    IReadOnlyDictionary<string, string> PhaseStops { get; }
}
