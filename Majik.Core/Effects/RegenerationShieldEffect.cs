using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 701.18 — Regeneration shield. Replacement effect that consumes the
/// next destroy of the target permanent: instead, tap it, remove all
/// damage, and remove it from combat. Single-use (OneShot=true).
///
/// "Remove from combat" semantics: clears IsAttacking / IsBlocking flags
/// on the permanent (combat manager checks these).
/// </summary>
public sealed class RegenerationShieldEffect : IReplacementEffect<DestroyIntent>
{
    private readonly Permanent _target;

    public RegenerationShieldEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public bool OneShot => true;
    public object? Tag => this;

    public bool Applies(DestroyIntent intent, IReadOnlyList<object> history) =>
        ReferenceEquals(intent.Target, _target);

    public DestroyIntent? Replace(DestroyIntent intent, IReadOnlyList<object> history)
    {
        if (!_target.IsTapped) _target.Tap();
        if (_target is Creature c) c.ClearDamage();
        // "Remove from combat" is owned by CombatFlow's per-turn plan; this
        // MVP cancels destroy + taps + clears damage. Combat-removal will
        // be wired when CombatFlow exposes a per-creature removal hook.
        return null;
    }
}
