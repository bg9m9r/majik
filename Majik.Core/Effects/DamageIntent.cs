using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// "Would deal damage" intent passed through <see cref="ReplacementBus"/>
/// before damage is actually applied (CR 614 + 615 prevention).
/// One of <see cref="TargetCreature"/>, <see cref="TargetPlayer"/>, or
/// <see cref="TargetPlaneswalker"/> is set.
/// </summary>
public sealed record DamageIntent(
    object Source,
    int Amount,
    Creature? TargetCreature = null,
    Player? TargetPlayer = null,
    Planeswalker? TargetPlaneswalker = null);
