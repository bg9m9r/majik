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
}
