using Majik.Core.Cards;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Blockers declared this combat: each blocker assigned to one attacker
/// (in damage-ordering order, blocker first declared takes damage first).
/// </summary>
public sealed record BlockPlan(IReadOnlyList<BlockerDeclaration> Blockers)
{
    public static readonly BlockPlan None = new(Array.Empty<BlockerDeclaration>());
}

public sealed record BlockerDeclaration(Creature Blocker, Creature Attacker);
