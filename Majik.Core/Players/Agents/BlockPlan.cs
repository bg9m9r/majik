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

/// <summary>
/// One declared blocker → blocked-attacker pairing. Both typed
/// <see cref="Permanent"/> (not <see cref="Creature"/>) so an animated
/// NON-creature combatant (a manland) can block, and the blocked attacker may
/// itself be an animated land (deferral
/// <c>animated-noncreature-as-combatant</c>, 4B). A real <see cref="Creature"/>
/// is a <see cref="Permanent"/>, so existing callers assign unchanged.
/// </summary>
public sealed record BlockerDeclaration(Permanent Blocker, Permanent Attacker);
