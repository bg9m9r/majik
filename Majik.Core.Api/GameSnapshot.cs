using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;

namespace Majik.Core.Api;

/// <summary>
/// Full save record — current state DTO plus the append-only action log AND
/// the deterministic-reconstruction inputs (RNG seed + seat ids + an
/// instance-id → card-name map). Sufficient to reconstitute the game via
/// <see cref="GameFacade.FromSnapshot"/>: a fresh facade with the SAME
/// <see cref="Seed"/> + a fresh logical clock, fast-forwarded by re-applying
/// every <see cref="LoggedCommand"/> through the submit path with event
/// fan-out suppressed.
///
/// <para>Replay is STRUCTURAL, not id-identical: the rebuilt facade mints new
/// nondeterministic Guids (card instance ids, player ids, ability/stack ids),
/// so the logged commands' verbatim ids are rebound at replay time —
/// <see cref="AliceId"/>/<see cref="BobId"/> map each command's
/// <see cref="GameCommand.PlayerId"/> back to a seat, and
/// <see cref="InstanceNames"/> maps each logged card instance id to its card
/// name so the rebuilt facade can resolve the equivalent live card. Full
/// id-identical replay (for client-facing rehydration) still needs the
/// deferred id-reseeding step; structural equivalence is sufficient here.</para>
/// </summary>
public sealed record GameSnapshot(
    GameStateDto State,
    IReadOnlyList<LoggedCommand> Log,
    // PLAN 08 / Phase 29.x — the pinned per-game RNG seed (Match.gameSeed,
    // GameRandom.Seed). Re-applied to a fresh GameRandom in FromSnapshot so
    // shuffles / coin flips / dice replay identically. Defaults to 0 so a
    // snapshot serialized before the seed plumbing still deserializes.
    int Seed = 0,
    // Original seat ids at capture time. FromSnapshot rebinds each logged
    // command's PlayerId (Alice-slot vs Bob-slot) to the rebuilt facade's
    // freshly-minted seat ids. Default empty for legacy snapshots.
    Guid AliceId = default,
    Guid BobId = default,
    // Map of every card instance id that appeared in the action log (in a
    // command's ids) → that card's name at capture time. FromSnapshot uses it
    // to translate id-bearing commands (PlayLand / Cast / DeclareAttackers /
    // target / mana picks …) onto the rebuilt facade's live cards by name +
    // position. Null/empty for legacy snapshots and id-free command logs.
    IReadOnlyDictionary<Guid, string>? InstanceNames = null);

public sealed record LoggedCommand(DateTime At, GameCommand Command);
