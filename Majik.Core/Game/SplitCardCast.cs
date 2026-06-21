using Majik.Core.Abilities;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

namespace Majik.Core.Game;

/// <summary>
/// CR 712 (split cards) + CR 702.102 (Fuse) — the declarative split-card cast
/// surface. A split card has two faces (halves) printed on one card; the
/// caster picks WHICH half/halves to cast:
///
/// <list type="bullet">
///   <item><b>Left half only</b> — cast that half at its printed cost
///     (CR 712.4a). The combined object behaves as the left half.</item>
///   <item><b>Right half only</b> — cast that half at its printed cost.</item>
///   <item><b>Fuse — BOTH halves</b> (CR 702.102) — cast both halves from
///     hand as ONE split spell, paying the COMBINED mana cost of both halves
///     (CR 702.102b) and choosing targets for both halves (CR 702.102c). On
///     resolution the spell does everything BOTH halves would do, in the
///     printed order — left half first, then right half (CR 702.102e).</item>
/// </list>
///
/// <para>
/// This helper composes the existing single-half <see cref="SpellDefinition"/>s
/// (each produced by the per-half factory, e.g. Wear / Tear) into the cast the
/// chosen mode demands. The single-half casts are returned unchanged (the
/// engine already handles a single-half cast — it is just an ordinary spell);
/// the FUSED cast is the new surface:
/// </para>
///
/// <para>
/// A fused <see cref="SpellDefinition"/> carries BOTH halves' target requests
/// — the left half's requests keyed to <c>ModeIndex 0</c> and the right half's
/// to <c>ModeIndex 1</c> — so <see cref="SpellCastFlow"/>'s index-aligned modal
/// target-collection path (which already keys collected targets by
/// <c>ModeIndex</c>) gathers targets for both halves through ONE cast pass
/// (CR 702.102c). Its <see cref="SpellDefinition.EffectFactory"/> re-slots each
/// half's targets back to that half's own slot 0 — so the per-half effect
/// closures (which read <c>Targets[0]</c>) need no change — and concatenates
/// the two halves' effects left-to-right (CR 702.102e).
/// </para>
///
/// <para>
/// The combined cost is surfaced via <see cref="FuseCost"/> — the field-wise
/// sum of both halves' printed costs (CR 702.102b) — which the caller passes to
/// the cast flow as the spell's effective cost. Both halves are still
/// independently castable via their own <c>[CardName]</c> factories; this
/// surface adds the previously-missing "both halves as one spell" line.
/// </para>
/// </summary>
public static class SplitCardCast
{
    /// <summary>
    /// CR 700.2d — the cast modes a Fuse split card offers. Numbered to match
    /// the <see cref="SpellDefinition.Modes"/> ordering the fused definition
    /// publishes (left = 0, right = 1).
    /// </summary>
    public enum Half
    {
        /// <summary>CR 712.4a — cast the left (front) half only.</summary>
        Left = 0,

        /// <summary>CR 712.4a — cast the right (back) half only.</summary>
        Right = 1,
    }

    /// <summary>
    /// CR 702.102b — the combined mana cost of both halves: the field-wise sum
    /// of the two printed costs (generic, each color, colorless, and the hybrid
    /// / Phyrexian pip lists concatenated). Pass the half cost strings as
    /// printed (e.g. "{1}{R}" and "{W}").
    /// </summary>
    public static ManaCost FuseCost(string leftCost, string rightCost)
    {
        ArgumentNullException.ThrowIfNull(leftCost);
        ArgumentNullException.ThrowIfNull(rightCost);
        return ManaCost.Parse(leftCost).Combine(ManaCost.Parse(rightCost));
    }

    /// <summary>
    /// CR 702.102 — build the FUSED <see cref="SpellDefinition"/> that casts
    /// BOTH halves as one split spell. The two single-half definitions are
    /// composed: each half's target requests are re-keyed to its mode index
    /// (left → 0, right → 1) so the cast flow collects targets for both halves
    /// in one pass (CR 702.102c), and the effect factory runs the left half's
    /// effects then the right half's effects (CR 702.102e), each fed its OWN
    /// targets.
    /// </summary>
    /// <param name="left">The left (front) half's single-cast definition
    /// (exactly what casting that half alone would use). Must declare at most
    /// one target request — split halves are single-target / untargeted.</param>
    /// <param name="right">The right (back) half's single-cast definition.</param>
    /// <param name="leftModeLabel">Human label for the left half (e.g.
    /// "Wear — destroy target artifact"). Surfaced on
    /// <see cref="SpellDefinition.Modes"/> for prompts/diagnostics only.</param>
    /// <param name="rightModeLabel">Human label for the right half.</param>
    public static SpellDefinition BuildFusedDefinition(
        SpellDefinition left,
        SpellDefinition right,
        string leftModeLabel,
        string rightModeLabel)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        // CR 712.4 — a split half is single-target or untargeted; the fused
        // target-collection path keys ONE slot per half by mode index, so a
        // half with more than one request can't be re-slotted unambiguously.
        if (left.TargetRequests.Count > 1 || right.TargetRequests.Count > 1)
        {
            throw new ArgumentException(
                "Fuse composition supports at most one target request per half " +
                $"(left has {left.TargetRequests.Count}, right has {right.TargetRequests.Count}).");
        }

        // CR 702.102c — both halves' target requests collected in one pass.
        // Re-key each half's (single) request to its mode index so
        // SpellCastFlow's index-aligned modal collection returns the chosen
        // targets at Targets[0] (left) / Targets[1] (right).
        var requests = new List<TargetRequest>(2);
        if (left.TargetRequests.Count == 1)
        {
            requests.Add(left.TargetRequests[0] with { ModeIndex = (int)Half.Left });
        }
        if (right.TargetRequests.Count == 1)
        {
            requests.Add(right.TargetRequests[0] with { ModeIndex = (int)Half.Right });
        }

        return new SpellDefinition(
            // CR 700.2d — both halves are always "chosen" for a fused cast, so
            // the modes are informational (the cast flow auto-chooses both, see
            // FusedModeChoice). They keep the cast flow on the index-aligned
            // modal target-collection path (TargetRequests carry ModeIndex).
            Modes: new[] { leftModeLabel, rightModeLabel },
            HasVariableX: left.HasVariableX || right.HasVariableX,
            TargetRequests: requests,
            // CR 702.102e — do everything BOTH halves would do, left then right.
            EffectFactory: chosen =>
            {
                var effects = new List<IEffect>();
                effects.AddRange(left.EffectFactory(SliceForHalf(chosen, Half.Left, left)));
                effects.AddRange(right.EffectFactory(SliceForHalf(chosen, Half.Right, right)));
                return effects;
            },
            MinModes: 2,
            MaxModes: 2);
    }

    /// <summary>
    /// CR 700.2d — the mode pick a fused cast announces: BOTH halves. Pass to
    /// <see cref="SpellCastFlow"/> so its modal target-collection path collects
    /// targets for both halves (left mode 0, right mode 1).
    /// </summary>
    public static IReadOnlyList<int> FusedModeChoice =>
        new[] { (int)Half.Left, (int)Half.Right };

    /// <summary>
    /// Re-slot one half's targets back to its own slot 0 so the per-half effect
    /// factory (which reads <c>Targets[0]</c>) sees its targets where it
    /// expects them. The fused params hold targets keyed by mode index
    /// (Targets[0] = left, Targets[1] = right); a half with no target request
    /// gets an empty target list.
    /// </summary>
    private static ChosenSpellParams SliceForHalf(
        ChosenSpellParams fused, Half half, SpellDefinition halfDef)
    {
        IReadOnlyList<IReadOnlyList<object>> halfTargets;
        if (halfDef.TargetRequests.Count == 0)
        {
            halfTargets = Array.Empty<IReadOnlyList<object>>();
        }
        else
        {
            var idx = (int)half;
            var slot = idx < fused.Targets.Count
                ? fused.Targets[idx]
                : (IReadOnlyList<object>)Array.Empty<object>();
            halfTargets = new[] { slot };
        }

        return fused with { Targets = halfTargets };
    }
}
