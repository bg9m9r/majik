using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Effects;

/// <summary>
/// CR 615 — "Prevent all damage that would be dealt to you." Cancels every
/// <see cref="DamageIntent"/> whose target is the source's controller. Backs
/// Solitary Confinement's persistent "Prevent all damage that would be dealt
/// to you" static.
///
/// Unlike <see cref="PreventAllDamageToYouAndYourPermanentsShield"/> — a
/// once-per-turn "this turn" prevention that auto-drops at cleanup — this
/// shield is NOT <see cref="IEndOfTurnExpirable"/>: it persists for as long
/// as its <paramref name="source"/> permanent remains on the battlefield
/// (CR 614.6 — a replacement is only active while its printed source is in
/// the right zone). It self-gates on <see cref="Permanent.Zone"/>, so the
/// owning factory only needs to <c>Register</c> it once — no explicit LTB
/// unregister (mirrors <see cref="WorshipDamageReplacement"/>). The beneficiary
/// is resolved from the source's CURRENT controller on every check, so a
/// control-change effect shifts the shield with the permanent (CR 702.18 is a
/// "you" effect = the controller).
///
/// Only the controller is shielded — permanents they control are NOT
/// (Solitary Confinement protects only "you"). Damage to creatures /
/// planeswalkers passes through untouched.
/// </summary>
public sealed class PreventAllDamageToPlayerShield : IReplacementEffect<DamageIntent>
{
    private readonly Permanent _source;

    public PreventAllDamageToPlayerShield(Permanent source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(DamageIntent intent, IReadOnlyList<object> history)
    {
        if (_source.Zone != ZoneType.Battlefield) return false;
        if (intent.Amount <= 0) return false;
        var controller = _source.Controller;
        return controller != null && ReferenceEquals(intent.TargetPlayer, controller);
    }

    // CR 615.1 — prevention cancels the damage entirely.
    public DamageIntent? Replace(DamageIntent intent, IReadOnlyList<object> history) => null;
}
