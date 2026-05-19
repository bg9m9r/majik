using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;

namespace Majik.Core.Api;

/// <summary>
/// Full save record — current state DTO plus the append-only action log.
/// Sufficient to reconstitute the game (replay = fresh facade + same seed +
/// each command in order). For Phase 29 first cut, only round-trip via
/// JSON is verified; deterministic re-execution lands once GameFacade
/// learns to consume an external log on construction (Phase 29.x).
/// </summary>
public sealed record GameSnapshot(
    GameStateDto State,
    IReadOnlyList<LoggedCommand> Log);

public sealed record LoggedCommand(DateTime At, GameCommand Command);
