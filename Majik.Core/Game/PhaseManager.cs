using Majik.Core.Combat;
using Majik.Core.Domain.Exceptions;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;

namespace Majik.Core.Game;

/// <summary>
/// Manages phase sequence and transitions within a turn.
/// </summary>
public class PhaseManager
{
    private readonly IEventBus? _eventBus;
    private readonly Queue<PhaseStateType> _extraPhases = new();
    
    private PhaseStateType? _currentPhase;
    private PhaseStateType[] _currentSequence = Array.Empty<PhaseStateType>();
    private int _currentPhaseIndex;
    private Player? _activePlayer;
    private bool _isFirstTurn;
    private CombatManager? _combatManager;

    /// <summary>
    /// The current phase.
    /// </summary>
    public PhaseStateType? CurrentPhase => _currentPhase;

    /// <summary>
    /// Whether the current phase can transition to the next.
    /// </summary>
    public bool CanTransition => _currentPhase != null;

    public PhaseManager(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>
    /// Set the combat manager for combat phase handling.
    /// </summary>
    public void SetCombatManager(CombatManager combatManager)
    {
        _combatManager = combatManager;
    }

    /// <summary>
    /// Initialize the phase manager for a new turn.
    /// </summary>
    public void InitializeForTurn(Player activePlayer, bool isFirstTurn)
    {
        _activePlayer = activePlayer;
        _isFirstTurn = isFirstTurn;
        _currentSequence = PhaseSequence.GetSequence(isFirstTurn);
        _currentPhaseIndex = 0;
        _currentPhase = null;
        _extraPhases.Clear();
    }

    /// <summary>
    /// Start the first phase of the turn.
    /// </summary>
    public void StartFirstPhase()
    {
        if (_activePlayer == null)
        {
            throw new InvalidGameStateException("Cannot start phase: no active player");
        }

        if (_currentSequence.Length == 0)
        {
            throw new InvalidGameStateException("Cannot start phase: no phase sequence defined");
        }

        _currentPhaseIndex = 0;
        _currentPhase = _currentSequence[0];
        
        _eventBus?.Publish(new PhaseStartedEvent(_currentPhase.Value, _activePlayer));
    }

    /// <summary>
    /// Transition to the next phase.
    /// </summary>
    public void TransitionToNextPhase()
    {
        if (_activePlayer == null)
        {
            throw new InvalidGameStateException("Cannot transition phase: no active player");
        }

        if (_currentPhase == null)
        {
            throw new InvalidGameStateException("Cannot transition phase: no current phase");
        }

        // End current phase
        var endingPhase = _currentPhase.Value;
        _eventBus?.Publish(new PhaseEndedEvent(endingPhase, _activePlayer));

        // Check for extra phases first
        if (_extraPhases.Count > 0)
        {
            var extraPhase = _extraPhases.Dequeue();
            _currentPhase = extraPhase;
            _eventBus?.Publish(new PhaseStartedEvent(extraPhase, _activePlayer));
            _eventBus?.Publish(new ExtraPhaseAddedEvent(extraPhase));
            return;
        }

        // Move to next phase in sequence
        _currentPhaseIndex++;
        
        if (_currentPhaseIndex >= _currentSequence.Length)
        {
            // Turn is complete
            _currentPhase = null;
            return;
        }

        _currentPhase = _currentSequence[_currentPhaseIndex];
        _eventBus?.Publish(new PhaseStartedEvent(_currentPhase.Value, _activePlayer));
    }

    /// <summary>
    /// Skip the current phase.
    /// </summary>
    public void SkipCurrentPhase()
    {
        if (_currentPhase == null)
        {
            throw new InvalidGameStateException("Cannot skip phase: no current phase");
        }

        // Just transition to next phase (effectively skipping)
        TransitionToNextPhase();
    }

    /// <summary>
    /// Add an extra phase to the queue.
    /// </summary>
    public void AddExtraPhase(PhaseStateType phase)
    {
        _extraPhases.Enqueue(phase);
        _eventBus?.Publish(new ExtraPhaseAddedEvent(phase));
    }

    /// <summary>
    /// Add an extra combat phase.
    /// </summary>
    public void AddExtraCombatPhase()
    {
        // Add all combat phases
        AddExtraPhase(PhaseStateType.BeginningOfCombat);
        AddExtraPhase(PhaseStateType.DeclareAttackers);
        AddExtraPhase(PhaseStateType.DeclareBlockers);
        AddExtraPhase(PhaseStateType.CombatDamage);
        AddExtraPhase(PhaseStateType.EndOfCombat);
    }

    /// <summary>
    /// Add an extra main phase.
    /// </summary>
    public void AddExtraMainPhase()
    {
        AddExtraPhase(PhaseStateType.Main);
    }

    /// <summary>
    /// Check if the turn is complete (all phases finished).
    /// </summary>
    public bool IsTurnComplete()
    {
        return _currentPhase == null && _extraPhases.Count == 0;
    }

    /// <summary>
    /// Get the next phase that will execute.
    /// </summary>
    public PhaseStateType? GetNextPhase()
    {
        if (_extraPhases.Count > 0)
        {
            return _extraPhases.Peek();
        }

        if (_currentPhaseIndex + 1 < _currentSequence.Length)
        {
            return _currentSequence[_currentPhaseIndex + 1];
        }

        return null;
    }

    /// <summary>
    /// Execute the current phase's logic.
    /// </summary>
    public void ExecuteCurrentPhase()
    {
        if (_currentPhase == null || _activePlayer == null)
        {
            return;
        }

        // Execute phase-specific logic based on phase type
        switch (_currentPhase.Value)
        {
            case PhaseStateType.Untap:
                // Untap logic will be implemented when we have permanents
                // For now, just auto-complete
                break;
                
            case PhaseStateType.Upkeep:
                // Upkeep triggers will be implemented when we have triggers
                // For now, just auto-complete
                break;
                
            case PhaseStateType.Draw:
                // Draw a card (if not first turn)
                if (!_isFirstTurn && _activePlayer.Zones.Library.GetCards().Any())
                {
                    var library = _activePlayer.Zones.Library;
                    var cards = library.GetCards().ToList();
                    if (cards.Count > 0)
                    {
                        var card = cards[0]; // Simplified: draw from top
                        // Card drawing will be handled by ZoneService in future
                        // For now, just mark as executed
                    }
                }
                break;
                
            case PhaseStateType.Main:
                // Main phase: players can cast spells (handled by stack/priority in future)
                // For now, just auto-complete
                break;
                
            case PhaseStateType.End:
                // End step triggers will be implemented when we have triggers
                // For now, just auto-complete
                break;
                
            case PhaseStateType.Cleanup:
                // Cleanup: discard to hand size, remove damage
                // For now, just auto-complete
                break;
                
            case PhaseStateType.BeginningOfCombat:
                // Beginning of Combat step (Rule 507)
                if (_combatManager != null && _activePlayer != null)
                {
                    _combatManager.StartCombat(_activePlayer);
                }
                break;

            case PhaseStateType.DeclareAttackers:
                // Declare Attackers step (Rule 508)
                // Player must declare attackers - handled by Game.DeclareAttackers()
                // Phase will wait for player action
                break;

            case PhaseStateType.DeclareBlockers:
                // Declare Blockers step (Rule 509)
                // Defending player must declare blockers - handled by Game.DeclareBlockers()
                // Phase will wait for player action
                break;

            case PhaseStateType.CombatDamage:
                // Combat Damage step (Rule 510)
                // Only assign damage if combat is in progress (attackers were declared)
                if (_combatManager != null && _combatManager.IsInCombat)
                {
                    _combatManager.AssignCombatDamage();
                }
                break;

            case PhaseStateType.EndOfCombat:
                // End of Combat step (Rule 511)
                // Only end combat if combat is in progress
                if (_combatManager != null && _combatManager.IsInCombat)
                {
                    _combatManager.EndCombat();
                }
                break;

            default:
                // Other phases auto-complete for now
                break;
        }
    }

    /// <summary>
    /// Check if the current phase can auto-advance (no player input needed).
    /// </summary>
    public bool CanAutoAdvance(Majik.Core.Stack.Stack? stack = null, PriorityManager? priorityManager = null)
    {
        if (_currentPhase == null)
        {
            return false;
        }

        // Check if stack is empty (required for phase to end - Rule 500.2)
        if (stack != null && !stack.IsEmpty)
        {
            return false;
        }

        // Check if all players have passed (required for phase to end - Rule 117.4)
        if (priorityManager != null && !priorityManager.AllPlayersPassed)
        {
            return false;
        }

        // Most phases can auto-advance if stack is empty and all passed
        // Main phases and combat will require player input in future
        switch (_currentPhase.Value)
        {
            case PhaseStateType.Main:
                // Main phase requires stack empty and all players passed
                return stack?.IsEmpty == true && priorityManager?.AllPlayersPassed == true;
                
            case PhaseStateType.DeclareAttackers:
            case PhaseStateType.DeclareBlockers:
                // Combat phases will wait for player in future
                // For now, auto-advance if stack empty and all passed
                return stack?.IsEmpty == true && priorityManager?.AllPlayersPassed == true;
                
            default:
                // All other phases auto-advance if stack empty and all passed
                return stack?.IsEmpty != false && priorityManager?.AllPlayersPassed != false;
        }
    }

    /// <summary>
    /// Process all phases in the current turn automatically.
    /// </summary>
    public void ProcessAllPhases(Majik.Core.Stack.Stack? stack = null, PriorityManager? priorityManager = null)
    {
        while (!IsTurnComplete())
        {
            if (_currentPhase == null)
            {
                break;
            }

            // Execute current phase
            ExecuteCurrentPhase();

            // Initialize priority for phases that need it
            if (priorityManager != null && _activePlayer != null)
            {
                // Give priority to active player at beginning of phase (Rule 117.3a)
                // For phases without priority (Untap, certain Cleanup), skip this
                if (NeedsPriority(_currentPhase.Value))
                {
                    priorityManager.InitializeForPhase(_activePlayer);
                    
                    // Process priority and stack resolution
                    // Loop until stack is empty AND all players have passed
                    var resolver = new Services.StackResolver();
                    
                    // Only process priority if there's something on the stack
                    // Otherwise, just wait for all players to pass once
                    if (stack != null && !stack.IsEmpty)
                    {
                        // Process priority and resolve stack objects
                        while (!stack.IsEmpty)
                        {
                            // Process priority until all players pass
                            while (!priorityManager.AllPlayersPassed)
                            {
                                // Simulate players passing for now
                                // In future, this will wait for actual player input
                                priorityManager.PassPriority();
                            }
                            
                            // All players have passed - resolve top object
                            resolver.ResolveTop(stack);
                            // Active player gets priority after resolution (Rule 117.3b)
                            priorityManager.GivePriority(_activePlayer);
                            // Continue loop to process priority for next object
                        }
                    }
                    
                    // Process priority one final time to ensure all players pass
                    // (needed even when stack is empty, as players might want to act)
                    while (!priorityManager.AllPlayersPassed)
                    {
                        // Simulate players passing for now
                        // In future, this will wait for actual player input
                        priorityManager.PassPriority();
                    }
                }
            }

            // Auto-advance if phase can complete
            if (CanAutoAdvance(stack, priorityManager))
            {
                TransitionToNextPhase();
            }
            else
            {
                // Phase is waiting for player input or stack to resolve
                // In future, this will pause and wait
                // For now, still advance to keep things moving
                TransitionToNextPhase();
            }
        }
    }

    /// <summary>
    /// Check if a phase needs priority (Rule 117.3a).
    /// </summary>
    private static bool NeedsPriority(PhaseStateType phase)
    {
        // Phases that don't give priority: Untap (Rule 502.4), certain Cleanup steps (Rule 514.3)
        // For now, only Untap doesn't give priority
        // Cleanup will be handled in future when we implement cleanup logic
        return phase != PhaseStateType.Untap;
    }
}
