using Majik.Core.Api.Commands;

namespace Majik.Server.Matches;

/// <summary>
/// Defensive input bounds for player-submitted <see cref="GameCommand"/>s.
///
/// These DTOs are deserialized straight off the wire and dispatched into the
/// engine. None of them carry server-enforced bounds, so a malicious client
/// could send a <see cref="ChooseXCommand"/> with an enormous <c>X</c> (the
/// engine pays/loops on X in several places) or a
/// <see cref="DeclareAttackersCommand"/> / <see cref="DeclareBlockersCommand"/>
/// / <see cref="CastSpellCommand"/> carrying a multi-million-element list,
/// forcing large allocations and CPU spins before the engine ever rejects the
/// illegal action. That is a cheap DoS vector.
///
/// The caps here are deliberately generous: a real game never has a hand,
/// battlefield, or stack anywhere near 64 objects, and no legitimate spell or
/// ability resolves with X above a few dozen. Bots and the engine never
/// legitimately exceed these. So well-behaved clients see no behavior change —
/// this only rejects pathological input. Rejection happens BEFORE the command
/// is dispatched to the engine, surfacing as <c>invalid-command</c> (HTTP 400),
/// not a 500 or an OOM.
/// </summary>
public static class CommandValidator
{
    /// <summary>Upper bound for <see cref="ChooseXCommand.X"/>. X is the
    /// value plugged into {X} costs/effects; the engine loops/pays on it.
    /// No real card resolves with X anywhere near this — 1000 is far above
    /// any reachable mana total but bounds the work the engine can be forced
    /// to do.</summary>
    public const int MaxX = 1000;

    /// <summary>Lower bound for <see cref="ChooseXCommand.X"/>. CR 107.3 — X
    /// is 0 when unspecified; a negative X is never legal input.</summary>
    public const int MinX = 0;

    /// <summary>Max length of any target / attacker / blocker / source /
    /// trigger-order / identifier list on a command. The largest legitimate
    /// list (e.g. declaring blockers for a wide board, ordering triggers) is
    /// bounded by battlefield/stack size, which is far below 64.</summary>
    public const int MaxListLength = 64;

    /// <summary>
    /// Validate a command's input bounds. Returns a <see cref="MatchError"/>
    /// ("invalid-command", HTTP 400) describing the first violation, or
    /// <see langword="null"/> when the command is within bounds. Does NOT
    /// validate game-legality (controller, zone, target legality, etc.) —
    /// that stays in the engine; this is purely a DoS / input-size guard.
    /// </summary>
    public static MatchError? Validate(GameCommand command) => command switch
    {
        ChooseXCommand x when x.X < MinX || x.X > MaxX =>
            new MatchError("invalid-command", $"X out of range [{MinX}, {MaxX}]."),

        CastSpellCommand cs when ListTooLong(cs.TargetInstanceIds?.Count) =>
            TooLong("targets"),
        CastSpellCommand cs when cs.XValue is int xv && (xv < MinX || xv > MaxX) =>
            new MatchError("invalid-command", $"X out of range [{MinX}, {MaxX}]."),

        ChooseTargetsCommand t when ListTooLong(t.TargetInstanceIds?.Count) =>
            TooLong("targets"),
        ChooseManaCommand mp when ListTooLong(mp.SourceInstanceIds?.Count) =>
            TooLong("mana sources"),
        OrderTriggersCommand ot when ListTooLong(ot.StackObjectIdsInOrder?.Count) =>
            TooLong("trigger order"),
        DeclareAttackersCommand da when ListTooLong(da.Attackers?.Count) =>
            TooLong("attackers"),
        DeclareBlockersCommand db when ListTooLong(db.Blockers?.Count) =>
            TooLong("blockers"),
        ChooseCardsToBottomCommand cb when ListTooLong(cb.CardInstanceIds?.Count) =>
            TooLong("cards to bottom"),

        _ => null,
    };

    private static bool ListTooLong(int? count) => count is int c && c > MaxListLength;

    private static MatchError TooLong(string what) =>
        new("invalid-command", $"Too many {what} (max {MaxListLength}).");
}
