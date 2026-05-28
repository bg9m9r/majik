namespace Majik.Server.Composition;

/// <summary>
/// Named rate-limit policy identifiers used across endpoint registrations.
/// <list type="bullet">
///   <item><see cref="Expensive"/> — 60 req/min per-sub. Protects abuse-prone
///   or write-heavy routes: deck CRUD, match creation/join, card search,
///   profile mutations.</item>
///   <item><see cref="InMatch"/> — 600 req/min per-sub. Applied to per-priority-
///   window routes (commands, state polls, roll, play-draw, concede, replay)
///   that are called multiple times per turn during a live game.</item>
/// </list>
/// Health, whoami, OpenAPI, and SignalR negotiate endpoints carry no policy.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>60 req/min — expensive / write-heavy / abuse-prone routes.</summary>
    public const string Expensive = "expensive";

    /// <summary>600 req/min — per-priority-window in-match gameplay routes.</summary>
    public const string InMatch = "in-match";
}
