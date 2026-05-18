using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Combat;

/// <summary>
/// Service for managing combat.
/// Coordinates all combat operations according to Magic: The Gathering rules (Rule 506-511).
/// </summary>
public class CombatManager
{
    private readonly IEventBus? _eventBus;
    private readonly CombatValidator _validator;
    private readonly StateBasedActions? _stateBasedActions;
    private readonly ZoneService? _zoneService;

    private Combat? _currentCombat;

    /// <summary>
    /// The current combat instance.
    /// </summary>
    public Combat? CurrentCombat => _currentCombat;

    /// <summary>
    /// Whether combat is currently active.
    /// </summary>
    public bool IsInCombat => _currentCombat != null && !_currentCombat.IsEnded;

    public CombatManager(IEventBus? eventBus = null, StateBasedActions? stateBasedActions = null, ZoneService? zoneService = null)
    {
        _eventBus = eventBus;
        _validator = new CombatValidator();
        _stateBasedActions = stateBasedActions;
        _zoneService = zoneService;
    }

    /// <summary>
    /// Start a new combat (Rule 507: Beginning of Combat step).
    /// </summary>
    public void StartCombat(Player activePlayer)
    {
        if (activePlayer == null)
        {
            throw new ArgumentNullException(nameof(activePlayer));
        }

        if (IsInCombat)
        {
            throw new InvalidGameStateException("Combat is already in progress");
        }

        // Combat starts with no attackers declared yet
        // The target will be determined when attackers are declared
        _currentCombat = null; // Will be created when attackers are declared

        _eventBus?.Publish(new CombatStartedEvent(activePlayer));
    }

    /// <summary>
    /// Declare attackers (Rule 508: Declare Attackers step).
    /// </summary>
    public void DeclareAttackers(Player activePlayer, IEnumerable<AttackerDeclaration> declarations)
    {
        if (activePlayer == null)
        {
            throw new ArgumentNullException(nameof(activePlayer));
        }

        if (declarations == null)
        {
            throw new ArgumentNullException(nameof(declarations));
        }

        var declarationList = declarations.ToList();

        // Determine target from first attacker
        Player? targetPlayer = null;
        Planeswalker? targetPlaneswalker = null;

        if (declarationList.Count > 0)
        {
            var firstDecl = declarationList[0];
            targetPlayer = firstDecl.TargetPlayer;
            targetPlaneswalker = firstDecl.TargetPlaneswalker;
        }

        // Validate all attackers
        var attackers = declarationList.Select(d => d.Creature).ToList();
        if (!_validator.IsValidAttackDeclaration(attackers, activePlayer, targetPlayer, targetPlaneswalker))
        {
            throw new InvalidPlayerActionException("Invalid attacker declaration");
        }

        // Create combat instance
        _currentCombat = new Combat(activePlayer, targetPlayer, targetPlaneswalker);

        // Create attacker objects and add to combat
        foreach (var declaration in declarationList)
        {
            var hasFirstStrike = CombatAbilities.HasFirstStrike(declaration.Creature);
            var hasDoubleStrike = CombatAbilities.HasDoubleStrike(declaration.Creature);
            var hasTrample = CombatAbilities.HasTrample(declaration.Creature);
            var hasDeathtouch = CombatAbilities.HasDeathtouch(declaration.Creature);
            var hasVigilance = CombatAbilities.HasVigilance(declaration.Creature);

            var attacker = new Attacker(
                declaration.Creature,
                declaration.TargetPlayer,
                declaration.TargetPlaneswalker,
                hasFirstStrike,
                hasDoubleStrike,
                hasTrample,
                hasDeathtouch,
                hasVigilance);

            _currentCombat.AddAttacker(attacker);

            // Tap attacker (unless has vigilance) (Rule 508.1k)
            if (!hasVigilance)
            {
                declaration.Creature.Tap();
            }
        }

        _currentCombat.TransitionToDeclaringBlockers();

        _eventBus?.Publish(new AttackersDeclaredEvent(_currentCombat));
    }

    /// <summary>
    /// Declare blockers (Rule 509: Declare Blockers step).
    /// </summary>
    public void DeclareBlockers(Player defendingPlayer, IEnumerable<BlockerDeclaration> declarations)
    {
        if (defendingPlayer == null)
        {
            throw new ArgumentNullException(nameof(defendingPlayer));
        }

        if (declarations == null)
        {
            throw new ArgumentNullException(nameof(declarations));
        }

        if (_currentCombat == null)
        {
            throw new InvalidGameStateException("No combat in progress");
        }

        if (_currentCombat.State != CombatState.DeclaringBlockers)
        {
            throw new InvalidGameStateException($"Cannot declare blockers in state {_currentCombat.State}");
        }

        var declarationList = declarations.ToList();

        // Validate all blockers
        var blocks = declarationList.Select(d => (d.Creature, d.Attacker)).ToList();
        if (!_validator.IsValidBlockDeclaration(blocks, defendingPlayer))
        {
            throw new InvalidPlayerActionException("Invalid blocker declaration");
        }

        // Create blocker objects and add to attackers
        foreach (var declaration in declarationList)
        {
            var hasFirstStrike = CombatAbilities.HasFirstStrike(declaration.Creature);
            var hasDoubleStrike = CombatAbilities.HasDoubleStrike(declaration.Creature);
            var hasDeathtouch = CombatAbilities.HasDeathtouch(declaration.Creature);

            var blocker = new Blocker(
                declaration.Creature,
                declaration.Attacker,
                hasFirstStrike,
                hasDoubleStrike,
                hasDeathtouch);

            declaration.Attacker.AddBlocker(blocker);
        }

        _currentCombat.TransitionToAssigningDamage();

        _eventBus?.Publish(new BlockersDeclaredEvent(_currentCombat));
    }

    /// <summary>
    /// Assign combat damage (Rule 510: Combat Damage step).
    /// </summary>
    public void AssignCombatDamage()
    {
        if (_currentCombat == null)
        {
            throw new InvalidGameStateException("No combat in progress");
        }

        if (_currentCombat.State != CombatState.AssigningDamage)
        {
            throw new InvalidGameStateException($"Cannot assign damage in state {_currentCombat.State}");
        }

        // First strike damage step (if applicable)
        if (HasFirstStrikeDamage(_currentCombat))
        {
            AssignFirstStrikeDamage(_currentCombat);
            ResolveCombatDamage(_currentCombat, isFirstStrike: true);
            
            // Check state-based actions after first strike damage
            if (_stateBasedActions != null && _currentCombat.AttackingPlayer != null)
            {
                var allPlayers = new[] { _currentCombat.AttackingPlayer, _currentCombat.DefendingPlayer }
                    .Where(p => p != null)
                    .Cast<Player>();
                var allCards = GetAllCombatCreatures(_currentCombat);
                _stateBasedActions.CheckStateBasedActions(allPlayers, allCards);
            }

            // Reset damage assignment for regular damage step
            ResetDamageAssignment(_currentCombat);
        }

        // Regular damage step
        AssignRegularDamage(_currentCombat);
        ResolveCombatDamage(_currentCombat, isFirstStrike: false);

        // Check state-based actions after regular damage
        if (_stateBasedActions != null && _currentCombat.AttackingPlayer != null)
        {
            var allPlayers = new[] { _currentCombat.AttackingPlayer, _currentCombat.DefendingPlayer }
                .Where(p => p != null)
                .Cast<Player>();
            var allCards = GetAllCombatCreatures(_currentCombat);
            _stateBasedActions.CheckStateBasedActions(allPlayers, allCards);
        }

        _currentCombat.TransitionToResolvingDamage();
    }

    /// <summary>
    /// Check if there are any first strike creatures in combat.
    /// </summary>
    private bool HasFirstStrikeDamage(Combat combat)
    {
        return combat.Attackers.Any(a => a.CanDealFirstStrikeDamage()) ||
               combat.GetAllBlockers().Any(b => b.CanDealFirstStrikeDamage());
    }

    /// <summary>
    /// Assign first strike damage.
    /// </summary>
    private void AssignFirstStrikeDamage(Combat combat)
    {
        foreach (var attacker in combat.Attackers)
        {
            if (attacker.CanDealFirstStrikeDamage())
            {
                AssignAttackerDamage(attacker);
            }
        }
    }

    /// <summary>
    /// Assign regular damage.
    /// </summary>
    private void AssignRegularDamage(Combat combat)
    {
        foreach (var attacker in combat.Attackers)
        {
            if (attacker.CanDealRegularDamage())
            {
                AssignAttackerDamage(attacker);
            }
        }
    }

    /// <summary>
    /// Assign damage from an attacker to blockers and/or target.
    /// </summary>
    private void AssignAttackerDamage(Attacker attacker)
    {
        int remainingPower = attacker.GetPower();

        if (attacker.Blockers.Count == 0)
        {
            // Unblocked: all damage to target
            attacker.AssignDamage(remainingPower);
            return;
        }

        // Blocked: assign damage to blockers
        // Without trample, all damage must be assigned to blockers
        // With trample, only lethal damage must be assigned to each blocker
        foreach (var blocker in attacker.Blockers)
        {
            if (remainingPower <= 0) break;

            int lethalDamage = CalculateLethalDamage(blocker.Creature, attacker.HasDeathtouch);
            int assignedDamage;
            
            if (attacker.HasTrample)
            {
                // With trample: assign only lethal damage, excess goes to target
                assignedDamage = Math.Min(lethalDamage, remainingPower);
            }
            else
            {
                // Without trample: assign all remaining power to this blocker
                assignedDamage = remainingPower;
            }

            blocker.AssignDamage(assignedDamage);
            attacker.AssignDamage(assignedDamage);
            remainingPower -= assignedDamage;
        }

        // Trample: excess damage to target
        if (attacker.HasTrample && remainingPower > 0)
        {
            attacker.AssignDamage(remainingPower);
        }
    }

    /// <summary>
    /// Calculate lethal damage for a creature.
    /// </summary>
    private int CalculateLethalDamage(Creature creature, bool hasDeathtouch)
    {
        if (hasDeathtouch)
        {
            return 1; // Deathtouch: 1 damage is lethal
        }

        return creature.Toughness;
    }

    /// <summary>
    /// Resolve combat damage (apply damage to creatures, players, planeswalkers).
    /// </summary>
    private void ResolveCombatDamage(Combat combat, bool isFirstStrike)
    {
        foreach (var attacker in combat.Attackers)
        {
            // Deal damage to blockers
            foreach (var blocker in attacker.Blockers)
            {
                if (blocker.AssignedDamage > 0)
                {
                    blocker.Creature.TakeDamage(blocker.AssignedDamage);
                    _eventBus?.Publish(new CombatDamageDealtEvent(
                        attacker.Creature, blocker.Creature, blocker.AssignedDamage, isFirstStrike));
                }
            }

            // Deal damage to target (unblocked or trample)
            int targetDamage = attacker.AssignedDamage - attacker.Blockers.Sum(b => b.AssignedDamage);

            if (targetDamage > 0)
            {
                if (attacker.TargetPlayer != null)
                {
                    attacker.TargetPlayer.LoseLife(targetDamage);
                    _eventBus?.Publish(new CombatDamageDealtEvent(
                        attacker.Creature, attacker.TargetPlayer, targetDamage, isFirstStrike));
                }
                else if (attacker.TargetPlaneswalker != null)
                {
                    attacker.TargetPlaneswalker.RemoveLoyalty(targetDamage);
                    _eventBus?.Publish(new CombatDamageDealtEvent(
                        attacker.Creature, attacker.TargetPlaneswalker, targetDamage, isFirstStrike));
                }
            }

            // Deal damage to attacker from blockers
            foreach (var blocker in attacker.Blockers)
            {
                if (blocker.Creature.Power > 0)
                {
                    attacker.Creature.TakeDamage(blocker.Creature.Power);
                    _eventBus?.Publish(new CombatDamageDealtEvent(
                        blocker.Creature, attacker.Creature, blocker.Creature.Power, isFirstStrike));
                }
            }
        }
    }

    /// <summary>
    /// Reset damage assignment for a new damage step.
    /// </summary>
    private void ResetDamageAssignment(Combat combat)
    {
        foreach (var attacker in combat.Attackers)
        {
            attacker.ResetDamageAssignment();
            foreach (var blocker in attacker.Blockers)
            {
                blocker.ResetDamageAssignment();
            }
        }
    }

    /// <summary>
    /// Get all creatures involved in combat.
    /// </summary>
    private IEnumerable<ICard> GetAllCombatCreatures(Combat combat)
    {
        var creatures = new List<ICard>();
        
        foreach (var attacker in combat.Attackers)
        {
            creatures.Add(attacker.Creature);
        }

        foreach (var blocker in combat.GetAllBlockers())
        {
            creatures.Add(blocker.Creature);
        }

        return creatures;
    }

    /// <summary>
    /// End combat (Rule 511: End of Combat step).
    /// </summary>
    public void EndCombat()
    {
        if (_currentCombat == null)
        {
            return; // No combat to end
        }

        _eventBus?.Publish(new CombatEndedEvent(_currentCombat));
        _currentCombat.End();
        _currentCombat = null;
    }

    /// <summary>
    /// Get valid attackers for a player.
    /// </summary>
    public IEnumerable<Creature> GetValidAttackers(Player player)
    {
        if (player == null)
        {
            return Enumerable.Empty<Creature>();
        }

        // Get all creatures controlled by player on battlefield
        // TODO: Get from ZoneService or player's battlefield zone
        return Enumerable.Empty<Creature>();
    }

    /// <summary>
    /// Get valid blockers for a player against an attacker.
    /// </summary>
    public IEnumerable<Creature> GetValidBlockers(Player player, Attacker attacker)
    {
        if (player == null || attacker == null)
        {
            return Enumerable.Empty<Creature>();
        }

        // Get all creatures controlled by player on battlefield that can block attacker
        // TODO: Get from ZoneService or player's battlefield zone
        return Enumerable.Empty<Creature>();
    }
}

/// <summary>
/// Declaration of an attacker.
/// </summary>
public class AttackerDeclaration
{
    public Creature Creature { get; }
    public Player? TargetPlayer { get; }
    public Planeswalker? TargetPlaneswalker { get; }

    public AttackerDeclaration(Creature creature, Player? targetPlayer = null, Planeswalker? targetPlaneswalker = null)
    {
        Creature = creature ?? throw new ArgumentNullException(nameof(creature));
        TargetPlayer = targetPlayer;
        TargetPlaneswalker = targetPlaneswalker;

        if (targetPlayer == null && targetPlaneswalker == null)
        {
            throw new ArgumentException("Must specify either target player or target planeswalker");
        }
    }
}

/// <summary>
/// Declaration of a blocker.
/// </summary>
public class BlockerDeclaration
{
    public Creature Creature { get; }
    public Attacker Attacker { get; }

    public BlockerDeclaration(Creature creature, Attacker attacker)
    {
        Creature = creature ?? throw new ArgumentNullException(nameof(creature));
        Attacker = attacker ?? throw new ArgumentNullException(nameof(attacker));
    }
}
