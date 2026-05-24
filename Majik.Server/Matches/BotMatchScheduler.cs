using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Majik.Server.Matches;

/// <summary>
/// Production implementation of <see cref="IBotMatchScheduler"/>. Uses
/// fire-and-forget <see cref="Task.Run(Func{Task})"/> with
/// <see cref="Task.Delay(TimeSpan)"/> dwells between events, mirroring the
/// pattern in <see cref="MatchTimeoutScheduler"/>. Each scheduled callback
/// opens a fresh DI scope to resolve <see cref="MatchService"/> (which is
/// scoped) before invoking the public API.
///
/// <para>Default delays — 600ms before the bot rolls, 800ms before the bot
/// chooses play/draw — are tuned so the user has time to read the rolling
/// state ("…") and the resolved dice values before the match transitions
/// into Playing. Override via constructor params for tests or tuning.</para>
/// </summary>
public sealed class BotMatchScheduler : IBotMatchScheduler
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BotMatchScheduler>? _logger;
    private readonly TimeSpan _rollDelay;
    private readonly TimeSpan _playDrawDelay;

    public BotMatchScheduler(
        IServiceProvider services,
        ILogger<BotMatchScheduler>? logger = null,
        TimeSpan? rollDelay = null,
        TimeSpan? playDrawDelay = null)
    {
        _services = services;
        _logger = logger;
        _rollDelay = rollDelay ?? TimeSpan.FromMilliseconds(600);
        _playDrawDelay = playDrawDelay ?? TimeSpan.FromMilliseconds(800);
    }

    public void ScheduleBotRoll(Guid matchId, string botSub)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_rollDelay > TimeSpan.Zero)
                    await Task.Delay(_rollDelay).ConfigureAwait(false);

                using var scope = _services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<MatchService>();
                await svc.SubmitRollAsync(botSub, matchId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Bot roll callback faulted. MatchId={MatchId} BotSub={BotSub}",
                    matchId, botSub);
            }
        });
    }

    public void ScheduleBotPlayDraw(Guid matchId, string botSub)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_playDrawDelay > TimeSpan.Zero)
                    await Task.Delay(_playDrawDelay).ConfigureAwait(false);

                using var scope = _services.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<MatchService>();
                // Bot is greedy: always choose "play" (going first).
                await svc.PlayDrawAsync(botSub, matchId, new PlayDrawRequest("play"), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Bot play/draw callback faulted. MatchId={MatchId} BotSub={BotSub}",
                    matchId, botSub);
            }
        });
    }
}

/// <summary>
/// Test implementation that invokes the bot actions synchronously
/// on the calling thread by resolving <see cref="MatchService"/> from a
/// fresh DI scope. Used in integration tests so the entire bot flow
/// completes inside the same <see cref="MatchService.CreateAsync"/> /
/// <see cref="MatchService.SubmitRollAsync"/> call that triggered it —
/// assertions can fire immediately after the caller returns without
/// polling.
/// </summary>
public sealed class SynchronousBotMatchScheduler : IBotMatchScheduler
{
    private readonly IServiceProvider _services;

    public SynchronousBotMatchScheduler(IServiceProvider services)
    {
        _services = services;
    }

    public void ScheduleBotRoll(Guid matchId, string botSub)
    {
        using var scope = _services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MatchService>();
        svc.SubmitRollAsync(botSub, matchId, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public void ScheduleBotPlayDraw(Guid matchId, string botSub)
    {
        using var scope = _services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<MatchService>();
        svc.PlayDrawAsync(botSub, matchId, new PlayDrawRequest("play"), CancellationToken.None)
            .GetAwaiter().GetResult();
    }
}

/// <summary>
/// Unit-test implementation that invokes the bot actions synchronously
/// against a directly-injected <see cref="MatchService"/>. Used in
/// unit-style tests (constructed via <c>new MatchService(...)</c>) where
/// there is no DI container. The <c>MatchService</c> reference is bound
/// after both objects are constructed via <see cref="Bind"/> (chicken /
/// egg with the scoped MatchService constructor parameter).
/// </summary>
public sealed class ImmediateBotMatchScheduler : IBotMatchScheduler
{
    private MatchService? _svc;

    public void Bind(MatchService svc) => _svc = svc;

    public void ScheduleBotRoll(Guid matchId, string botSub)
    {
        if (_svc == null) return;
        _svc.SubmitRollAsync(botSub, matchId, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public void ScheduleBotPlayDraw(Guid matchId, string botSub)
    {
        if (_svc == null) return;
        _svc.PlayDrawAsync(botSub, matchId, new PlayDrawRequest("play"), CancellationToken.None)
            .GetAwaiter().GetResult();
    }
}

/// <summary>No-op scheduler. Used by tests that don't want the bot driver
/// to fire at all (e.g. asserting state immediately after a transition,
/// without follow-on roll/playdraw effects).</summary>
public sealed class NullBotMatchScheduler : IBotMatchScheduler
{
    public static readonly NullBotMatchScheduler Instance = new();
    public void ScheduleBotRoll(Guid matchId, string botSub) { }
    public void ScheduleBotPlayDraw(Guid matchId, string botSub) { }
}
