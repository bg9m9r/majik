namespace Majik.Core.Abilities;

/// <summary>
/// Interface for effects that can be executed when spells or abilities resolve.
///
/// <para>
/// PLAN 01 — the canonical execution surface is the asynchronous
/// <see cref="ExecuteAsync"/>, which receives the live
/// <see cref="ResolutionContext"/> (controller, agent, game, chosen targets).
/// The synchronous <see cref="Execute"/> is retained as a thin default-
/// implemented shim over <see cref="ExecuteAsync"/> so the thousands of
/// existing factories and tests that build effects from the legacy
/// <c>Effect(string, Action)</c> ctor — and that drive them via
/// <c>effect.Execute()</c> — keep compiling and running unchanged. The
/// sync shim runs on the context-free <see cref="ResolutionContext.Legacy"/>
/// frame; effects that need the agent / live game must override
/// <see cref="ExecuteAsync"/>.
/// </para>
/// </summary>
public interface IEffect
{
    /// <summary>
    /// Description of the effect.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Execute the effect against the live resolution context (CR 608).
    /// </summary>
    ValueTask ExecuteAsync(ResolutionContext ctx);

    /// <summary>
    /// Legacy synchronous execution. Default shim over <see cref="ExecuteAsync"/>
    /// on the context-free <see cref="ResolutionContext.Legacy"/> frame.
    /// Retained for the ~1300 existing factories + ~2600 test call sites that
    /// run self-contained sync effects; new code should prefer the async path.
    /// </summary>
    void Execute() => ExecuteAsync(ResolutionContext.Legacy).GetAwaiter().GetResult();
}
