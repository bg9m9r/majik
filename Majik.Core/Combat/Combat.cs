using Majik.Core.Players;

namespace Majik.Core.Combat;

/// <summary>
/// Represents a combat instance.
/// Encapsulates combat state and participants.
/// </summary>
public class Combat
{
    private readonly List<Attacker> _attackers = new();

    /// <summary>
    /// The player who is attacking (active player).
    /// </summary>
    public Player AttackingPlayer { get; }

    /// <summary>
    /// The player being attacked (if attacking player).
    /// </summary>
    public Player? DefendingPlayer { get; }

    /// <summary>
    /// The planeswalker being attacked (if attacking planeswalker).
    /// </summary>
    public Cards.Planeswalker? TargetPlaneswalker { get; }

    /// <summary>
    /// All attacking creatures.
    /// </summary>
    public IReadOnlyList<Attacker> Attackers => _attackers.AsReadOnly();

    /// <summary>
    /// The current state of combat.
    /// </summary>
    public CombatState State { get; private set; }

    /// <summary>
    /// When combat started.
    /// </summary>
    public DateTime Timestamp { get; }

    public Combat(Player attackingPlayer, Player? defendingPlayer = null, Cards.Planeswalker? targetPlaneswalker = null)
    {
        if (attackingPlayer == null)
        {
            throw new ArgumentNullException(nameof(attackingPlayer));
        }

        if (defendingPlayer == null && targetPlaneswalker == null)
        {
            throw new ArgumentException("Must have either defending player or target planeswalker", nameof(defendingPlayer));
        }

        if (defendingPlayer != null && targetPlaneswalker != null)
        {
            throw new ArgumentException("Cannot attack both player and planeswalker in same combat", nameof(defendingPlayer));
        }

        AttackingPlayer = attackingPlayer;
        DefendingPlayer = defendingPlayer;
        TargetPlaneswalker = targetPlaneswalker;
        State = CombatState.DeclaringAttackers;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Add an attacker to this combat.
    /// </summary>
    public void AddAttacker(Attacker attacker)
    {
        if (attacker == null)
        {
            throw new ArgumentNullException(nameof(attacker));
        }

        if (State != CombatState.DeclaringAttackers)
        {
            throw new InvalidOperationException($"Cannot add attacker in state {State}");
        }

        if (attacker.TargetPlayer != DefendingPlayer && attacker.TargetPlaneswalker != TargetPlaneswalker)
        {
            throw new ArgumentException("Attacker target does not match combat target", nameof(attacker));
        }

        if (_attackers.Contains(attacker))
        {
            return; // Already added
        }

        _attackers.Add(attacker);
    }

    /// <summary>
    /// CR 508.3g — splice a creature into this combat as an attacker while
    /// combat is already in progress (used by effects that "put a creature
    /// onto the battlefield tapped and attacking" — Mobilize, Geist of Saint
    /// Traft's Angel, etc.). Unlike <see cref="AddAttacker"/>, this is legal
    /// in any state before combat ends because the creature was never
    /// "declared" in the declare-attackers step — it bypasses declaration
    /// and goes straight into the attacker set. The token still must attack
    /// the same defending player / planeswalker as the rest of the combat
    /// (CR 508.4 — a creature put onto the battlefield attacking is attacking
    /// the player or planeswalker the effect that created it specifies, which
    /// for Mobilize is the same defender its creator is attacking).
    /// </summary>
    public void AddAttackerInProgress(Attacker attacker)
    {
        if (attacker == null)
        {
            throw new ArgumentNullException(nameof(attacker));
        }

        if (State == CombatState.Resolved)
        {
            throw new InvalidOperationException(
                "Cannot add an attacker to a combat that has ended");
        }

        if (attacker.TargetPlayer != DefendingPlayer && attacker.TargetPlaneswalker != TargetPlaneswalker)
        {
            throw new ArgumentException("Attacker target does not match combat target", nameof(attacker));
        }

        if (_attackers.Contains(attacker))
        {
            return; // Already added
        }

        _attackers.Add(attacker);
    }

    /// <summary>
    /// Transition to declaring blockers state.
    /// </summary>
    public void TransitionToDeclaringBlockers()
    {
        if (State != CombatState.DeclaringAttackers)
        {
            throw new InvalidOperationException($"Cannot transition to declaring blockers from state {State}");
        }

        State = CombatState.DeclaringBlockers;
    }

    /// <summary>
    /// Transition to assigning damage state.
    /// </summary>
    public void TransitionToAssigningDamage()
    {
        if (State != CombatState.DeclaringBlockers)
        {
            throw new InvalidOperationException($"Cannot transition to assigning damage from state {State}");
        }

        State = CombatState.AssigningDamage;
    }

    /// <summary>
    /// Transition to resolving damage state.
    /// </summary>
    public void TransitionToResolvingDamage()
    {
        if (State != CombatState.AssigningDamage)
        {
            throw new InvalidOperationException($"Cannot transition to resolving damage from state {State}");
        }

        State = CombatState.ResolvingDamage;
    }

    /// <summary>
    /// End combat.
    /// </summary>
    public void End()
    {
        State = CombatState.Resolved;
    }

    /// <summary>
    /// Check if combat has ended.
    /// </summary>
    public bool IsEnded => State == CombatState.Resolved;

    /// <summary>
    /// Get all blockers in this combat.
    /// </summary>
    public IEnumerable<Blocker> GetAllBlockers()
    {
        return _attackers.SelectMany(a => a.Blockers);
    }

    /// <summary>
    /// CR 506.4 — remove the attacker entry for <paramref name="creature"/>
    /// from this combat (e.g. it was returned to hand / left the battlefield,
    /// as Ninjutsu does to the unblocked attacker it bounces — CR 702.49e).
    /// Returns true if an entry was removed.
    /// </summary>
    public bool RemoveAttacker(Cards.Creature creature)
    {
        if (creature == null) throw new ArgumentNullException(nameof(creature));
        return _attackers.RemoveAll(a => ReferenceEquals(a.Creature, creature)) > 0;
    }
}
