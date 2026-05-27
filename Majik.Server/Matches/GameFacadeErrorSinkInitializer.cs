using Majik.Core.Api;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Matches;

/// <summary>
/// Installs a structured-logging sink onto <see cref="GameFacade.OnEventHandlerError"/>
/// at startup so per-match <c>EventBus</c> handler exceptions are surfaced
/// loudly instead of vanishing. Majik.Core takes no dependency on
/// Microsoft.Extensions.Logging, so the sink is bridged here in the server
/// composition layer.
///
/// <para>The hook is process-wide (a new <c>EventBus</c> is created per
/// <see cref="GameFacade"/>, all of which read this static delegate), so it
/// is set once on start and cleared on stop to keep test hosts hermetic.</para>
/// </summary>
public sealed class GameFacadeErrorSinkInitializer : IHostedService
{
    private readonly ILogger<GameFacadeErrorSinkInitializer> _logger;

    public GameFacadeErrorSinkInitializer(ILogger<GameFacadeErrorSinkInitializer> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        GameFacade.OnEventHandlerError = (ex, @event) =>
        {
            // ERROR, not Critical: one bad handler doesn't abort delivery to
            // the rest of the engine (the bus stays isolating), but a thrown
            // handler is a real defect that must never be silent.
            _logger.LogError(ex,
                "Engine EventBus handler threw while delivering {EventType} (EventId={EventId}). " +
                "Delivery to other handlers continued; this is a defect that must be investigated.",
                @event.GetType().Name, @event.EventId);
        };
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        GameFacade.OnEventHandlerError = null;
        return Task.CompletedTask;
    }
}
