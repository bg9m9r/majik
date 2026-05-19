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
    public abstract bool MayAttack(Creature attacker, object defender);
}

/// <summary>Pay-N-per-attacker restriction (Ghostly Prison / Propaganda).
/// The protected defender is set at construction; <see cref="MayAttack"/>
/// returns true only if the attacker's controller has pre-deposited the
/// required mana via <see cref="MarkPaid"/>.</summary>
public sealed class PayPerAttackerRestriction : AttackRestriction
{
    private readonly Player _protectedPlayer;
    private readonly ManaCost _costPerAttacker;
    private readonly HashSet<Creature> _paid = new();

    public PayPerAttackerRestriction(Player protectedPlayer, ManaCost costPerAttacker)
    {
        _protectedPlayer = protectedPlayer ?? throw new ArgumentNullException(nameof(protectedPlayer));
        _costPerAttacker = costPerAttacker ?? throw new ArgumentNullException(nameof(costPerAttacker));
    }

    public ManaCost CostPerAttacker => _costPerAttacker;

    public override bool Protects(object defender) => ReferenceEquals(defender, _protectedPlayer);

    public override bool MayAttack(Creature attacker, object defender)
    {
        if (!Protects(defender)) return true;
        return _paid.Contains(attacker);
    }

    /// <summary>Engine calls this after the attacker's controller pays the
    /// per-attacker cost. The mark is consumed when combat resets via
    /// <see cref="ClearForTurn"/>.</summary>
    public void MarkPaid(Creature attacker) => _paid.Add(attacker);

    public void ClearForTurn() => _paid.Clear();
}

/// <summary>Active attack restrictions. CombatValidator consults this.</summary>
public sealed class AttackRestrictionRegistry
{
    private readonly List<AttackRestriction> _entries = new();
    public IReadOnlyList<AttackRestriction> Active => _entries.AsReadOnly();
    public void Register(AttackRestriction r) => _entries.Add(r);
    public void Unregister(AttackRestriction r) => _entries.Remove(r);

    public bool MayAttack(Creature attacker, object defender) =>
        _entries.All(r => r.MayAttack(attacker, defender));
}
