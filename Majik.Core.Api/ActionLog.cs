using Majik.Core.Api.Commands;

namespace Majik.Core.Api;

/// <summary>
/// Append-only log of every command submitted to a <see cref="GameFacade"/>.
/// Paired with the seeded <see cref="Majik.Core.Random.GameRandom"/> and the
/// initial game options, this log is sufficient to deterministically replay
/// a game (Phase 29 first cut).
/// </summary>
public sealed class ActionLog
{
    private readonly List<LoggedAction> _actions = new();

    public IReadOnlyList<LoggedAction> Actions => _actions.AsReadOnly();
    public int Count => _actions.Count;

    public void Append(GameCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        _actions.Add(new LoggedAction(DateTime.UtcNow, command));
    }

    public sealed record LoggedAction(DateTime At, GameCommand Command);
}
