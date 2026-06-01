namespace Majik.Server.Matches.Persistence;

/// <summary>
/// PLAN 08 (body) — feature flag for durable engine-state persistence
/// (command-log + checkpoints + claim→rehydrate). Bound from the
/// <c>EnginePersistence</c> configuration section.
///
/// <para><b>DEFAULT OFF.</b> With the flag off, behaviour is exactly as today:
/// no durable writes happen on command submit, no checkpoints are taken, and a
/// command that misses the in-process <c>GameRegistry</c> falls back to the
/// existing "game-not-started" path (single-process, in-memory only). The
/// deploy / auto-deploy posture is unchanged until an operator flips this on.
/// Flipping it on is purely additive — it enables the durable command log, the
/// periodic checkpoints, and the rehydrate-on-miss path.</para>
/// </summary>
public sealed class EnginePersistenceOptions
{
    public const string SectionName = "EnginePersistence";

    /// <summary>Master switch. Default false → no durable writes, no rehydrate.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Take a checkpoint (SaveSnapshot + last applied seq) every N accepted
    /// commands. A checkpoint bounds how many commands a rehydration must
    /// replay. Default 25. Ignored when <see cref="Enabled"/> is false.
    /// </summary>
    public int CheckpointEveryCommands { get; set; } = 25;
}
