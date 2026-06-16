namespace Majik.Server.Matches;

/// <summary>
/// Why the engine game-loop was aborted by wedge supervision. Maps to the
/// (non-OpenAPI) SignalR <c>match.engine-error</c> wire value:
/// <list type="bullet">
///   <item><see cref="Fault"/> — the fire-and-forget loop task threw
///   (autonomous progression between human submits faulted) → <c>"engine-fault"</c>.</item>
///   <item><see cref="Hang"/> — the loop made no progress for the watchdog
///   window with no pending human prompt (a bot decision never returned) →
///   <c>"engine-hang"</c>.</item>
/// </list>
/// The triggering exception (when any) is logged server-side only and never
/// crosses the wire.
/// </summary>
public enum EngineFaultReason
{
    Fault,
    Hang,
}
