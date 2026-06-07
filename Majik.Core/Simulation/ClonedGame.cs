using Majik.Core.Cards;
using Majik.Core.Game;
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

    /// <summary>
    /// Cloned stack, or null if no live stack was provided to
    /// <see cref="GameStateCloner.Clone"/>. Only <see cref="Majik.Core.Spells.Spell"/>
    /// stack objects are cloned; activated/triggered abilities are not carried over
    /// (see GameStateCloner for the escalation note).
    /// </summary>
    public Majik.Core.Stack.Stack? Stack { get; init; }

    /// <summary>
    /// Cloned per-turn tally, or null if no live TurnState was provided.
    /// </summary>
    public TurnState? TurnState { get; init; }

    public Player PlayerFor(Player original) => PlayerMap[original];
}
