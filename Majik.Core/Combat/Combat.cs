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
    /// The planeswalker being attacked (if attacking planeswalker). Typed
    /// <see cref="Cards.Permanent"/> so a creature-front DFC flipped to its
    /// planeswalker back (CR 711, <see cref="Cards.Permanent.IsEffectivePlaneswalker"/>)
    /// is a legal combat defender too.
    /// </summary>
    public Cards.Permanent? TargetPlaneswalker { get; }

    /// <summary>
    /// All attacking creatures.
    /// </summary>
    public IReadOnlyList<Attacker> Attackers => _attackers.AsReadOnly();

    /// <summary>
    /// CR 506.2 / 508.4 — the distinct set of <b>defenders</b> being attacked in
    /// this combat, derived purely from the per-attacker band fields
    /// (<see cref="Attacker.TargetPlayer"/> / <see cref="Attacker.TargetPlaneswalker"/>).
    /// A player attacked directly contributes themselves; a planeswalker being
    /// attacked contributes its <see cref="Player"/> controller (CR 508.4 — the
    /// defending player of an attacked planeswalker is the player who controls
    /// it). The combat's nominal <see cref="DefendingPlayer"/> /
    /// <see cref="TargetPlaneswalker"/> is included too, so a combat with no
    /// attackers yet still reports the defender it was declared against.
    /// Order-preserving, reference-deduped.
    ///
    /// This is the per-opponent enumeration seam that "for each opponent ...
    /// attacking that player or a planeswalker they control"
    /// (<c>Adeline, Resplendent Cathar</c>) keys its band count on — no new
    /// combat subsystem, just an enumeration over the existing
    /// <c>Attacker.Target*</c> fields the model already carries. In the engine's
    /// 2-player model this is a single defender; the projection generalises to
    /// the multiplayer per-opponent bands without changing the storage model.
    /// </summary>
    public IReadOnlyList<Player> AttackedDefenders
    {
        get
        {
            var defenders = new List<Player>();

            void Add(Player? p)
            {
                if (p == null) return;
                if (defenders.Any(d => ReferenceEquals(d, p))) return;
                defenders.Add(p);
            }

            // The combat's nominal defender (the player it was declared against,
            // or the controller of the attacked planeswalker).
            Add(DefendingPlayer);
            Add(TargetPlaneswalker?.Controller);

            // Each attacker's own band — a token spliced in attacking a
            // planeswalker the defending player controls resolves to that same
            // controller (deduped above).
            foreach (var attacker in _attackers)
            {
                Add(attacker.TargetPlayer);
                Add(attacker.TargetPlaneswalker?.Controller);
            }

            return defenders.AsReadOnly();
        }
    }

    /// <summary>
    /// The current state of combat.
    /// </summary>
    public CombatState State { get; private set; }

    /// <summary>
    /// When combat started.
    /// </summary>
    public DateTime Timestamp { get; }

    public Combat(Player attackingPlayer, Player? defendingPlayer = null, Cards.Permanent? targetPlaneswalker = null)
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
    ///
    /// CR 508.4 also permits an effect to put the token onto the battlefield
    /// attacking <b>a different permitted defender belonging to the same
    /// defending player</b> — e.g. Adeline's token attacking "that player OR a
    /// planeswalker they control". A token whose <see cref="Attacker.TargetPlaneswalker"/>
    /// is controlled by this combat's <see cref="DefendingPlayer"/> is therefore
    /// accepted even though the combat's own band targets the player directly.
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

        if (!IsPermittedInProgressTarget(attacker))
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
    /// CR 508.4 — whether <paramref name="attacker"/> may be spliced into this
    /// in-progress combat. Legal targets are the combat's own defender (the
    /// <see cref="DefendingPlayer"/> or <see cref="TargetPlaneswalker"/>) OR a
    /// planeswalker controlled by this combat's defending player (a band against
    /// "a planeswalker they control"). The defending player is the controller of
    /// the combat's attacked planeswalker when the combat targets a walker.
    /// </summary>
    private bool IsPermittedInProgressTarget(Attacker attacker)
    {
        // Matches the combat's exact band (player or the same planeswalker).
        if (ReferenceEquals(attacker.TargetPlayer, DefendingPlayer)) return true;
        if (ReferenceEquals(attacker.TargetPlaneswalker, TargetPlaneswalker) &&
            TargetPlaneswalker != null) return true;

        // A planeswalker controlled by the same defending player is permitted
        // (Adeline — "that player OR a planeswalker they control").
        var defender = DefendingPlayer ?? TargetPlaneswalker?.Controller;
        if (attacker.TargetPlaneswalker != null && defender != null &&
            ReferenceEquals(attacker.TargetPlaneswalker.Controller, defender))
        {
            return true;
        }

        // A direct-player band against the same defending player.
        if (attacker.TargetPlayer != null && defender != null &&
            ReferenceEquals(attacker.TargetPlayer, defender))
        {
            return true;
        }

        return false;
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
