using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — "Prevent all damage that would be dealt to you and permanents
/// you control this turn." Cancels every <see cref="DamageIntent"/> whose
/// target is the beneficiary player OR a permanent that player controls,
/// for the remainder of the turn. Backs the Endure / Safe Passage
/// (creatures-you-control variant) family.
///
/// Target classification reads the three target fields on
/// <see cref="DamageIntent"/>:
///   - <see cref="DamageIntent.TargetPlayer"/> matches the beneficiary, OR
///   - <see cref="DamageIntent.TargetCreature"/> / <see cref="DamageIntent.TargetPlaneswalker"/>
///     is controlled by the beneficiary.
///
/// "Creatures you control" (Safe Passage) is treated identically to
/// "permanents you control" at v1 — every damage intent the engine
/// currently routes here is creature/player/planeswalker-bound, so the
/// broader filter is a strict superset.
///
/// Auto-drops at cleanup via <see cref="IEndOfTurnExpirable"/>.
/// </summary>
public sealed class PreventAllDamageToYouAndYourPermanentsShield
    : IReplacementEffect<DamageIntent>, IEndOfTurnExpirable
{
    private readonly Player _beneficiary;

    public PreventAllDamageToYouAndYourPermanentsShield(Player beneficiary)
    {
        _beneficiary = beneficiary ?? throw new ArgumentNullException(nameof(beneficiary));
    }

    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (intent.Amount <= 0) return false;
        if (ReferenceEquals(intent.TargetPlayer, _beneficiary)) return true;
        if (intent.TargetCreature is Permanent c && ReferenceEquals(c.Controller, _beneficiary)) return true;
        if (intent.TargetPlaneswalker is Permanent pw && ReferenceEquals(pw.Controller, _beneficiary)) return true;
        return false;
    }

    // CR 615.1 — prevention cancels the damage entirely.
    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history) => null;
}
