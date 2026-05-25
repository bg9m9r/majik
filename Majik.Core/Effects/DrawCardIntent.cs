using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614 / CR 121 — "would draw a card" intent. <see cref="Primitives.Fx.DrawCards"/>
/// builds one of these per individual draw and routes it through the
/// drawing player's attached <see cref="ReplacementBus"/> (when present)
/// before mutating the library / hand. Replacement effects can:
/// <list type="bullet">
///   <item>cancel the draw outright (return <c>null</c>) — Dredge (CR 702.52),
///         "instead reveal the top card and ..." replacements, etc.;</item>
///   <item>redirect the draw to a different player (future — Alms Collector
///         style replacements);</item>
///   <item>let it through unchanged (the bus returns the same intent and
///         the caller commits the draw normally).</item>
/// </list>
/// Players without an attached bus continue to draw by the direct path —
/// every pre-existing caller of <see cref="Primitives.Fx.DrawCards"/> works
/// unchanged in the unwired posture (the intent is only constructed when
/// <see cref="Player.Replacements"/> is non-null).
/// </summary>
public sealed record DrawCardIntent(Player Player);
