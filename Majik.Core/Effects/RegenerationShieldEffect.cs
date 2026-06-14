using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 701.18 — Regeneration shield. Replacement effect that consumes the
/// next destroy of the target permanent: instead, tap it, remove all
/// damage, and remove it from combat. Single-use (OneShot=true).
///
/// "Remove from combat" semantics (CR 701.15c): the regenerated permanent is
/// dropped from the live combat-membership registry
/// (<see cref="Majik.Core.Combat.CombatMembershipRegistry"/>) so an in-combat
/// target gate (Eiganjo, Seat of the Empire's channel) stops treating it as a
/// legal "attacking or blocking creature".
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
        // CR 701.15c — remove the regenerated permanent from combat via the live
        // combat-membership registry's per-creature removal hook (combat for the
        // rest of the attackers/blockers continues).
        Majik.Core.Combat.CombatMembershipRegistryProvider.Current.RemoveFromCombat(_target);
        return null;
    }
}
