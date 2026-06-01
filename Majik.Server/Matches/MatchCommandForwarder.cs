// ─────────────────────────────────────────────────────────────────────────
// DORMANT on the current deploy. This Redis pub/sub cross-replica command
// forwarder is part of the horizontal scale-out chain and is INERT in
// production today: the live deploy runs a SINGLE majik-api instance
// (numInstances unset) with NO Redis provisioned, so every method here is a
// no-op and SendAsync returns false — MatchService always dispatches into its
// own in-memory GameFacade and never forwards. It exists so multi-replica
// scaling is a future config change rather than a rewrite, and so a reader of
// the command path knows these branches are not exercised on the
// single-instance deploy. No behaviour change on the current topology.
// ─────────────────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using System.Text.Json;
using Majik.Core.Api.Commands;
using Majik.Server.Composition;
using StackExchange.Redis;

namespace Majik.Server.Matches;

/// <summary>
/// Cross-replica command forwarder over Redis pub/sub. Lets any replica
/// accept an HTTP <c>POST /matches/{id}/commands</c> regardless of which
/// replica owns the in-memory <c>GameFacade</c>: non-owners publish the
/// command on <c>cmd:match:{id}</c>, the owner is subscribed to that
/// channel and dispatches into its local facade, then publishes the
/// result on the sender's per-instance reply channel
/// <c>cmd:reply:{instanceId}</c>.
///
/// Lifecycle:
/// - Owner: <see cref="OnClaimedAsync"/> after claiming ownership;
///   <see cref="OnReleasedAsync"/> before/after release.
/// - Sender: subscribes to its own reply channel once at startup.
///
/// When Redis isn't configured, all methods are inert. <see cref="SendAsync"/>
/// returns false so MatchService falls back to its existing
/// "game-not-started" error. This keeps single-replica deploys unchanged.
/// </summary>
public interface IMatchCommandForwarder
{
    /// <summary>Start listening for forwarded commands for this match.
    /// Idempotent; safe to call repeatedly when ownership is reclaimed.</summary>
    Task OnClaimedAsync(Guid matchId, CancellationToken ct);

    /// <summary>Stop listening for forwarded commands for this match.</summary>
    Task OnReleasedAsync(Guid matchId, CancellationToken ct);

    /// <summary>Forward a command to whichever replica currently owns the
    /// match. Returns true on remote success; false when the owner times
    /// out or rejects the command (caller treats that as "game-not-started"
    /// or similar; we deliberately do not differentiate at this layer).</summary>
    Task<bool> SendAsync(Guid matchId, string callerSub, GameCommand command, CancellationToken ct);
}

public sealed class MatchCommandForwarder : IMatchCommandForwarder, IHostedService, IAsyncDisposable
{
    /// <summary>How long the sender waits for the owner's reply before
    /// giving up. Should be longer than typical engine command latency
    /// (most commands resolve in &lt; 100 ms) but short enough that a
    /// stuck owner doesn't tie up the sender for long.</summary>
    public static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer? _redis;
    private readonly IInstanceIdProvider _instanceIds;
    private readonly IServiceProvider _services;
    private readonly ILogger<MatchCommandForwarder> _logger;
    private readonly ConcurrentDictionary<Guid, ChannelMessageQueue> _ownedSubscriptions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ReplyMessage>> _pending = new();
    private ChannelMessageQueue? _replyQueue;

    public MatchCommandForwarder(
        IInstanceIdProvider instanceIds,
        IServiceProvider services,
        ILogger<MatchCommandForwarder> logger,
        IConnectionMultiplexer? redis = null)
    {
        _instanceIds = instanceIds;
        _services = services;
        _logger = logger;
        _redis = redis;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_redis == null) return;
        var sub = _redis.GetSubscriber();
        var channel = ReplyChannel(_instanceIds.Value);
        _replyQueue = await sub.SubscribeAsync(channel);
        _replyQueue.OnMessage(OnReplyReceived);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_replyQueue != null) await _replyQueue.UnsubscribeAsync();
        foreach (var q in _ownedSubscriptions.Values) await q.UnsubscribeAsync();
        _ownedSubscriptions.Clear();
    }

    public ValueTask DisposeAsync() => new(StopAsync(CancellationToken.None));

    public async Task OnClaimedAsync(Guid matchId, CancellationToken ct)
    {
        if (_redis == null) return;
        if (_ownedSubscriptions.ContainsKey(matchId)) return;

        var sub = _redis.GetSubscriber();
        var queue = await sub.SubscribeAsync(CommandChannel(matchId));
        queue.OnMessage(msg => OnCommandReceived(matchId, msg));
        _ownedSubscriptions[matchId] = queue;
    }

    public async Task OnReleasedAsync(Guid matchId, CancellationToken ct)
    {
        if (_ownedSubscriptions.TryRemove(matchId, out var queue))
        {
            await queue.UnsubscribeAsync();
        }
    }

    public async Task<bool> SendAsync(Guid matchId, string callerSub, GameCommand command, CancellationToken ct)
    {
        if (_redis == null) return false;

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ReplyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        try
        {
            var msg = new CommandMessage(
                requestId,
                _instanceIds.Value,
                callerSub,
                JsonSerializer.Serialize<GameCommand>(command, JsonOptions));
            var json = JsonSerializer.Serialize(msg, JsonOptions);
            var sub = _redis.GetSubscriber();
            var delivered = await sub.PublishAsync(CommandChannel(matchId), json);
            if (delivered == 0)
            {
                // No subscribers — nobody owns this match.
                return false;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ReplyTimeout);
            using var reg = timeoutCts.Token.Register(
                () => tcs.TrySetCanceled(timeoutCts.Token));

            var reply = await tcs.Task;
            return reply.Success;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Forwarded command timed out. MatchId={MatchId}", matchId);
            return false;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task OnCommandReceived(Guid matchId, ChannelMessage msg)
    {
        try
        {
            var cmd = JsonSerializer.Deserialize<CommandMessage>((string)msg.Message!, JsonOptions);
            if (cmd == null) return;

            var success = false;
            string? errorCode = null;
            try
            {
                var gameCommand = JsonSerializer.Deserialize<GameCommand>(cmd.CommandJson, JsonOptions);
                if (gameCommand != null)
                {
                    using var scope = _services.CreateScope();
                    var matchService = scope.ServiceProvider.GetRequiredService<MatchService>();
                    var result = await matchService.SubmitCommandAsync(
                        cmd.CallerSub, matchId, gameCommand, CancellationToken.None);
                    success = result.IsSuccess;
                    errorCode = result.IsSuccess ? null : result.Error?.Error;
                }
                else
                {
                    errorCode = "deserialize-failed";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Forwarded-command dispatch failed. MatchId={MatchId} RequestId={RequestId}",
                    matchId, cmd.RequestId);
                errorCode = "dispatch-failed";
            }

            var reply = new ReplyMessage(cmd.RequestId, success, errorCode);
            var json = JsonSerializer.Serialize(reply, JsonOptions);
            var sub = _redis!.GetSubscriber();
            await sub.PublishAsync(ReplyChannel(cmd.SenderInstanceId), json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forwarder failed to handle inbound command. MatchId={MatchId}", matchId);
        }
    }

    private void OnReplyReceived(ChannelMessage msg)
    {
        try
        {
            var reply = JsonSerializer.Deserialize<ReplyMessage>((string)msg.Message!, JsonOptions);
            if (reply == null) return;
            if (_pending.TryGetValue(reply.RequestId, out var tcs))
            {
                tcs.TrySetResult(reply);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forwarder failed to handle reply");
        }
    }

    private static RedisChannel CommandChannel(Guid matchId) =>
        RedisChannel.Literal($"cmd:match:{matchId:N}");

    private static RedisChannel ReplyChannel(string instanceId) =>
        RedisChannel.Literal($"cmd:reply:{instanceId}");

    internal sealed record CommandMessage(
        string RequestId,
        string SenderInstanceId,
        string CallerSub,
        string CommandJson);

    internal sealed record ReplyMessage(string RequestId, bool Success, string? ErrorCode);
}
