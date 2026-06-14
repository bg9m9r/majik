using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Players.Agents;

/// <summary>
/// Attackers declared this combat: which permanents attack and what they target
/// (each attacks either a defending player or a planeswalker).
/// </summary>
public sealed record CombatPlan(IReadOnlyList<AttackerDeclaration> Attackers)
{
    public static readonly CombatPlan None = new(Array.Empty<AttackerDeclaration>());
}

/// <summary>
/// One declared attacker. <see cref="Attacker"/> is typed <see cref="Permanent"/>
/// (not <see cref="Creature"/>) so an animated NON-creature C# instance — a
/// manland (a <see cref="Land"/> effectively a creature via a Layer-4 grant,
/// CR 613.1c) — can be declared as an attacker (deferral
/// <c>animated-noncreature-as-combatant</c>, 4B). A real <see cref="Creature"/>
/// is a <see cref="Permanent"/>, so existing callers assign unchanged.
/// </summary>
public sealed record AttackerDeclaration(Permanent Attacker, object DefendingPlayerOrPlaneswalker);
