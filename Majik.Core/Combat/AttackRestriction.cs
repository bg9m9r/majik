using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Combat;

/// <summary>
/// CR 506.3 — attack restrictions. A creature can't attack a defender
/// (player/planeswalker) if any active restriction returns false for the
/// proposed attack. Ghostly Prison ("Creatures can't attack you unless
/// their controller pays {2} for each creature attacking you") /
/// Propaganda / Norn's Annex all model the same shape: a paywall checked
/// at declare-attackers time.
///
/// Restrictions are registered with <see cref="AttackRestrictionRegistry"/>
/// and consulted by <see cref="CombatValidator"/> after baseline can-attack
/// rules (tapped/summoning-sick/defender) have passed.
/// </summary>
public abstract class AttackRestriction
{
    /// <summary>Player or planeswalker this restriction protects.</summary>
    public abstract bool Protects(object defender);

    /// <summary>True when <paramref name="attacker"/> may legally attack
    /// the protected target (the controller paid any required cost, etc.).</summary>
    public abstract bool MayAttack(Permanent attacker, object defender);
}

/// <summary>
/// Pay-N-per-attacker restriction (CR 508.1g) — the "attack-tax paywall"
/// behind Ghostly Prison / Propaganda / Sphere of Safety / Norn's Annex:
/// "Creatures can't attack [you / you or planeswalkers you control] unless
/// their controller pays {cost} for each creature [attacking that defender]."
///
/// The protected defender is the controlling player; optionally the
/// planeswalkers that player controls are also protected (Sphere of Safety —
/// "you or planeswalkers you control"). <see cref="MayAttack"/> returns true
/// only once the attacker's controller has paid the per-attacker cost via
/// <see cref="MarkPaid"/> — the engine charges this in
/// <see cref="CombatFlow"/> right after attackers are declared (CR 508.1g).
///
/// The cost is supplied as a <see cref="Func{ManaCost}"/> evaluated at
/// declare-attackers time so dynamic taxes (Sphere of Safety: {X} where X is
/// the number of enchantments the protected player controls) recompute against
/// the current board, while a flat tax (Ghostly Prison / Propaganda: {2}) is
/// just a constant-returning closure.
/// </summary>
public sealed class PayPerAttackerRestriction : AttackRestriction
{
    private readonly Player _protectedPlayer;
    private readonly Func<ManaCost> _costPerAttacker;
    private readonly bool _protectsPlaneswalkers;
    private readonly Func<bool>? _isActive;
    private readonly HashSet<Permanent> _paid = new();

    private PayPerAttackerRestriction(
        Player protectedPlayer,
        Func<ManaCost> costPerAttacker,
        bool protectsPlaneswalkers,
        Func<bool>? isActive = null)
    {
        _protectedPlayer = protectedPlayer ?? throw new ArgumentNullException(nameof(protectedPlayer));
        _costPerAttacker = costPerAttacker ?? throw new ArgumentNullException(nameof(costPerAttacker));
        _protectsPlaneswalkers = protectsPlaneswalkers;
        _isActive = isActive;
    }

    /// <summary>The protected player.</summary>
    public Player ProtectedPlayer => _protectedPlayer;

    /// <summary>True when this restriction also protects the planeswalkers the
    /// protected player controls (Sphere of Safety).</summary>
    public bool ProtectsPlaneswalkers => _protectsPlaneswalkers;

    /// <summary>Ghostly Prison / Propaganda — flat {cost} per attacker on the
    /// protected player only. <paramref name="isActive"/>, when supplied, gates
    /// the paywall on the source enchantment still being on the battlefield, so
    /// the restriction auto-deactivates when the enchantment leaves (no LTB
    /// unregister needed — mirrors Static Prison's zone-guarded replacement).</summary>
    public static PayPerAttackerRestriction FlatMana(
        Player protectedPlayer, ManaCost cost, Func<bool>? isActive = null)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return new PayPerAttackerRestriction(
            protectedPlayer, () => cost, protectsPlaneswalkers: false, isActive);
    }

    /// <summary>Sphere of Safety / Norn's Annex — dynamic per-attacker cost
    /// (recomputed at declare-attackers) plus optional planeswalker protection.
    /// <paramref name="isActive"/> gates the paywall on the source still being
    /// on the battlefield (see <see cref="FlatMana"/>).</summary>
    public static PayPerAttackerRestriction Dynamic(
        Player protectedPlayer,
        Func<ManaCost> costPerAttacker,
        bool protectsPlaneswalkers = false,
        Func<bool>? isActive = null)
        => new(protectedPlayer, costPerAttacker, protectsPlaneswalkers, isActive);

    /// <summary>CR 508.1g — the cost the controller must pay for each attacker
    /// attacking the protected defender, evaluated now (so a dynamic tax sees
    /// the current board).</summary>
    public ManaCost CostPerAttacker => _costPerAttacker();

    /// <summary>True when the paywall is currently in force (the source
    /// enchantment is on the battlefield). Always true when no
    /// <c>isActive</c> gate was supplied.</summary>
    public bool IsActive => _isActive?.Invoke() ?? true;

    public override bool Protects(object defender)
    {
        if (!IsActive) return false;
        if (ReferenceEquals(defender, _protectedPlayer)) return true;
        if (_protectsPlaneswalkers && defender is Planeswalker pw)
            return ReferenceEquals(pw.Controller, _protectedPlayer);
        return false;
    }

    public override bool MayAttack(Permanent attacker, object defender)
    {
        if (!Protects(defender)) return true;
        return _paid.Contains(attacker);
    }

    /// <summary>Engine calls this after the attacker's controller pays the
    /// per-attacker cost. The mark is consumed when combat resets via
    /// <see cref="ClearForTurn"/>.</summary>
    public void MarkPaid(Permanent attacker) => _paid.Add(attacker);

    public void ClearForTurn() => _paid.Clear();
}

/// <summary>Active attack restrictions. CombatValidator consults this.</summary>
public sealed class AttackRestrictionRegistry
{
    private readonly List<AttackRestriction> _entries = new();
    public IReadOnlyList<AttackRestriction> Active => _entries.AsReadOnly();
    public void Register(AttackRestriction r) => _entries.Add(r);
    public void Unregister(AttackRestriction r) => _entries.Remove(r);

    public bool MayAttack(Permanent attacker, object defender) =>
        _entries.All(r => r.MayAttack(attacker, defender));
}
