using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — "Prevent all combat damage that would be dealt to players
/// this turn." Narrower than <see cref="PreventAllCombatDamageShield"/>
/// (Fog) — only player-bound combat damage is cancelled; creature- and
/// planeswalker-bound combat damage still resolves.
///
/// Backs Commencement of Festivities / Defend the Hearth.
///
/// Auto-drops at cleanup via <see cref="IEndOfTurnExpirable"/>.
/// </summary>
public sealed class PreventAllCombatDamageToPlayersShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history) =>
        intent.Amount > 0
        && intent.Source is Creature
        && intent.TargetPlayer is not null;

    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history) => null;
}
