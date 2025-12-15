using Majik.Core.Events;

namespace Majik.Core.Services;

/// <summary>
/// Domain service for managing game operations.
/// Orchestrates game-level operations and coordinates state machines.
/// </summary>
public class GameService
{
    private readonly IEventBus? _eventBus;

    public GameService(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    // Game service methods can be added here as needed
    // For now, it's a placeholder for future game orchestration logic
}
