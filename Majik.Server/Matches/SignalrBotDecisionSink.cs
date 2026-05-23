using Majik.Bot.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Matches;

/// <summary>
/// <see cref="IBotDecisionSink"/> that forwards each
/// <see cref="BotDecision"/> to the SignalR match group on the
/// <c>"bot-decision"</c> channel. One instance per match — the matchId is
/// captured at construction so the publish is a pure function of the
/// decision record (no per-call routing logic).
///
/// <para>Group-broadcast is appropriate here: a bot decision describes
/// the bot's choice on the bot's own seat. There is no opponent-hidden
/// info in a <see cref="BotDecision"/> (no card identities beyond names
/// already on the battlefield/stack via existing events). Both seats can
/// see it — humans get "why the bot did X", spectators (future) get the
/// same.</para>
///
/// <para>Faulty publisher must not abort the engine — the underlying
/// hub publish is wrapped in try/catch (mirrors
/// <see cref="LoggerBotDecisionSink"/>'s contract and the bridge's
/// <c>ForwardEvent</c> error path).</para>
/// </summary>
public sealed class SignalrBotDecisionSink : IBotDecisionSink
{
    private readonly Guid _matchId;
    private readonly IMatchHubPublisher _hub;
    private readonly ILogger<SignalrBotDecisionSink>? _logger;

    public SignalrBotDecisionSink(
        Guid matchId,
        IMatchHubPublisher hub,
        ILogger<SignalrBotDecisionSink>? logger = null)
    {
        _matchId = matchId;
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger;
    }

    /// <summary>The SignalR channel name. Mirrors the
    /// <c>"event"</c> / <c>"prompt"</c> convention used by
    /// <see cref="MatchFacadeBridge"/>.</summary>
    public const string Channel = "bot-decision";

    public void Record(BotDecision decision)
    {
        try
        {
            // The DTO sent over the wire IS the BotDecision record.
            // System.Text.Json's default serializer handles the
            // sealed record + IReadOnlyList + IReadOnlyDictionary
            // shapes already (System.Collections.Generic types are
            // first-class). No additional contract is needed.
            _hub.Publish(_matchId, Channel, decision);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "SignalrBotDecisionSink: failed to publish bot decision. " +
                "MatchId={MatchId} DecisionType={DecisionType}",
                _matchId, decision.DecisionType);
        }
    }
}
