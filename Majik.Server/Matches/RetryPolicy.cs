using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Majik.Server.Matches;

/// <summary>
/// Bounded exponential-backoff retry for match-freezing writes. A transient
/// Mongo fault (connection blip, election, timeout) on a CAS that drives a
/// terminal transition (e.g. clock timeout → Completed) would otherwise
/// leave the match stuck in <c>Playing</c> forever. We retry the operation
/// a few times with growing delays; on exhaustion we log CRITICAL and
/// rethrow so the failure is loud, never silent.
///
/// <para>Retries MUST be safe to re-run. All call sites wrap CAS
/// (compare-and-swap) updates whose filter gates on the expected state, so a
/// lost-race retry that finds the state already advanced is a harmless
/// no-op (MatchedCount == 0 → returns false), never a double-apply.</para>
/// </summary>
public static class RetryPolicy
{
    /// <summary>Default attempt budget: 4 tries total (1 initial + 3 retries),
    /// backing off 100ms → 200ms → 400ms (capped at 1s).</summary>
    public const int DefaultMaxAttempts = 4;

    /// <summary>
    /// Run <paramref name="operation"/>, retrying on transient Mongo faults
    /// with exponential backoff. Non-transient exceptions propagate
    /// immediately (no point retrying a logic error). On exhausting the
    /// attempt budget the last transient exception is rethrown after a
    /// CRITICAL log.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ILogger? logger,
        string operationName,
        CancellationToken ct,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var @base = baseDelay ?? TimeSpan.FromMilliseconds(100);
        var cap = maxDelay ?? TimeSpan.FromSeconds(1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < maxAttempts)
            {
                // exponential backoff: base * 2^(attempt-1), capped.
                var delayMs = Math.Min(
                    cap.TotalMilliseconds,
                    @base.TotalMilliseconds * Math.Pow(2, attempt - 1));
                logger?.LogWarning(ex,
                    "Transient fault during {Operation} (attempt {Attempt}/{MaxAttempts}); " +
                    "retrying in {DelayMs}ms.",
                    operationName, attempt, maxAttempts, (int)delayMs);
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                // Budget exhausted on a transient fault — this is the
                // freeze-the-match case. Fail LOUD.
                logger?.LogCritical(ex,
                    "Exhausted {MaxAttempts} attempts on transient fault during {Operation}; " +
                    "the match may be left in a non-terminal state and require manual recovery.",
                    maxAttempts, operationName);
                throw;
            }
        }
    }

    /// <summary>Same as <see cref="ExecuteAsync{T}"/> for void operations.</summary>
    public static Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        ILogger? logger,
        string operationName,
        CancellationToken ct,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteAsync<bool>(async c => { await operation(c).ConfigureAwait(false); return true; },
            logger, operationName, ct, maxAttempts, baseDelay, maxDelay);
    }

    /// <summary>
    /// A fault is transient when retrying might succeed: Mongo connection /
    /// timeout / not-primary (election) errors. Driver-level
    /// <see cref="MongoException"/>s that carry the
    /// <c>TransientTransactionError</c> label, plus the connection/timeout
    /// exception types, all qualify. Logic errors (ArgumentException, etc.)
    /// do not.
    /// </summary>
    public static bool IsTransient(Exception ex) => ex switch
    {
        MongoConnectionException => true,
        MongoExecutionTimeoutException => true,
        MongoNotPrimaryException => true,
        MongoNodeIsRecoveringException => true,
        TimeoutException => true,
        MongoCommandException cmd => cmd.HasErrorLabel("TransientTransactionError"),
        MongoException me => me.HasErrorLabel("TransientTransactionError"),
        _ => false,
    };
}
