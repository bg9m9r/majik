using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// "Would deal damage" intent passed through <see cref="ReplacementBus"/>
/// before damage is actually applied (CR 614 + 615 prevention).
/// One of <see cref="TargetCreature"/>, <see cref="TargetPlayer"/>, or
/// <see cref="TargetPlaneswalker"/> is set.
///
/// <see cref="IsCombatDamage"/> discriminates combat damage (CR 510.1) from
/// every other damage-dealing path (spell resolution, activated/triggered
/// abilities, "ping" effects). Combat damage is stamped <c>true</c> by
/// <see cref="Majik.Core.Combat.CombatFlow"/> when it publishes the
/// per-creature damage intent during the combat damage step; every other
/// caller leaves the flag at its default <c>false</c>. Replacement effects
/// that specifically read combat damage (e.g. "If equipped creature would
/// deal combat damage, it deals double that damage instead" on Inquisitor's
/// Flail, the Fog family's "prevent all combat damage" shields) gate on
/// this flag rather than guessing from the source/target shape.
/// </summary>
public sealed record DamageIntent(
    object Source,
    int Amount,
    Creature? TargetCreature = null,
    Player? TargetPlayer = null,
    Planeswalker? TargetPlaneswalker = null)
{
    /// <summary>
    /// True when the intent was raised by combat damage assignment
    /// (CR 510.1). Combat damage is stamped <c>true</c> by
    /// <see cref="Majik.Core.Combat.CombatFlow"/>; non-combat sources
    /// (spells, activated/triggered abilities, ping effects) leave this
    /// at its default <c>false</c>.
    /// </summary>
    public bool IsCombatDamage { get; init; }
}
