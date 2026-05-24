using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614 / CR 119.6 — "would gain life" intent. <see cref="Player.GainLife"/>
/// builds one of these and routes it through the player's attached
/// <see cref="ReplacementBus"/> (when present) before mutating the life
/// total. Replacement effects can:
/// <list type="bullet">
///   <item>scale the gain (with X-gain riders),</item>
///   <item>reduce it to zero (Roiling Vortex's "players can't gain
///         life" static — implemented as a <see cref="LambdaReplacement{TIntent}"/>
///         that returns <c>intent with { Amount = 0 }</c>),</item>
///   <item>cancel it outright (returning <c>null</c>, e.g. a hypothetical
///         "you can't gain life" personal blocker).</item>
/// </list>
/// Players without an attached bus continue to gain life by the direct
/// path — every pre-existing caller of <see cref="Player.GainLife"/>
/// works unchanged in the unwired posture.
/// </summary>
public sealed record LifeGainIntent(Player Target, int Amount);
