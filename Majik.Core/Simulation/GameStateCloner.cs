using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Simulation;

/// <summary>
/// Deep-clones live game runtime state for sandbox simulation. Static card
/// DEFINITION data (oracle text, abilities, base mana cost, types) is shared
/// by reference — only per-game runtime state is copied. Two passes:
///   1. value-clone players + every card they own (no cross-references yet);
///   2. re-link reference fields (controller, attachments, stack targets)
///      through the InstanceId / player remap tables.
/// </summary>
public static class GameStateCloner
{
    public static ClonedGame Clone(IReadOnlyList<Player> players)
    {
        var playerMap = new Dictionary<Player, Player>();
        var cardMap = new Dictionary<Guid, ICard>();

        // Pass 1: empty player shells (life/name copied; zones empty).
        foreach (var p in players)
        {
            var clone = p.CloneEmpty();
            playerMap[p] = clone;
        }

        return new ClonedGame
        {
            Players = players.Select(p => playerMap[p]).ToList(),
            PlayerMap = playerMap,
            CardMap = cardMap,
        };
    }
}
