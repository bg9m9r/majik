using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 614.12 / CR 121.1 — "would draw one or more cards" quantity intent.
/// <see cref="Primitives.Fx.DrawCards"/> builds one of these ONCE per draw
/// instruction (before the per-card loop) and routes it through the
/// drawing player's attached <see cref="ReplacementBus"/> (when present).
/// Replacement effects that modify the <em>number</em> of cards a draw
/// instruction yields ride this intent rather than the per-individual-draw
/// <see cref="DrawCardIntent"/>:
/// <list type="bullet">
///   <item><b>"you draw that many cards plus one instead"</b> (Quantum
///         Riddler — bump <see cref="Count"/> by +1) — CR 614.12;</item>
///   <item><b>"draw two cards instead"</b> / "draw an additional card"
///         style scaling — set/add to <see cref="Count"/>;</item>
///   <item><b>"skip additional draws"</b> (Necrodominance — cap
///         <see cref="Count"/> at 1 when more than one is requested) —
///         CR 614.12.</item>
/// </list>
/// This is the quantity tier of a two-tier draw-replacement model. The
/// per-individual-draw cancel / redirect tier (Dredge — CR 702.52,
/// Spirit of the Labyrinth / Narset "can't draw more than one card each
/// turn") rides <see cref="DrawCardIntent"/>, published once per resolved
/// card AFTER this count intent has settled the requested quantity. Because
/// the two intents are different runtime types, the bus dispatches each to
/// only its own subscribers — count modifiers never see a per-card cancel
/// intent and vice versa, so neither tier disturbs the other.
///
/// <para>A replacement may also cancel the whole draw instruction by
/// returning <c>null</c> (zero draws), though most "can't draw" effects
/// prefer the per-card tier so partial / per-card riders still apply.</para>
///
/// <para>Players without an attached bus continue to draw by the direct
/// path — every pre-existing caller of <see cref="Primitives.Fx.DrawCards"/>
/// works unchanged in the unwired posture (the intent is only constructed
/// when <see cref="Player.Replacements"/> is non-null).</para>
/// </summary>
public sealed record DrawCountIntent(Player Player, int Count);
