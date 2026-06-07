using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Simulation;

/// <summary>
/// Result of <see cref="GameStateCloner.Clone"/>: cloned players plus the
/// remap tables the sandbox builder needs to re-link subsystems.
/// </summary>
public sealed class ClonedGame
{
    public required IReadOnlyList<Player> Players { get; init; }
    public required IReadOnlyDictionary<Player, Player> PlayerMap { get; init; }   // original -> clone
    public required IReadOnlyDictionary<Guid, ICard> CardMap { get; init; }        // InstanceId -> cloned card

    public Player PlayerFor(Player original) => PlayerMap[original];
}
