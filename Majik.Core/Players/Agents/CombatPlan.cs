using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Attackers declared this combat: which creatures attack and what they target
/// (each attacks either a defending player or a planeswalker).
/// </summary>
public sealed record CombatPlan(IReadOnlyList<AttackerDeclaration> Attackers)
{
    public static readonly CombatPlan None = new(Array.Empty<AttackerDeclaration>());
}

public sealed record AttackerDeclaration(Creature Attacker, object DefendingPlayerOrPlaneswalker);
