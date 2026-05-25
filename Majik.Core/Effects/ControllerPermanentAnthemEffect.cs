using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613 — generic "anthem-style" continuous effect that applies to every
/// permanent the source's controller controls. Mirrors
/// <see cref="LordStaticEffect"/> but with no creature/subtype filter, so
/// it covers non-creature permanents too (CR 613.1f keyword grants apply
/// to permanents regardless of type; P/T bonuses are silently no-ops when
/// the affected permanent isn't a creature).
///
/// <para>Canonical caller: Avacyn, Angel of Hope —
/// "Other permanents you control have indestructible." The effect is
/// registered while Avacyn is on the battlefield (per
/// <see cref="ContinuousEffect.IsActive"/>) and lifted on LTB via the
/// standard <see cref="ContinuousEffectsService"/> Prune / Unregister
/// pattern.</para>
///
/// <para>Selection predicate, evaluated on every permanent at every
/// recompute:
/// <list type="bullet">
///   <item><c>permanent.Controller == source.Controller</c> — symmetric
///     with CR 109.5 "you" / "your".</item>
///   <item><c>includeSelf || permanent != source</c> — "other permanents"
///     vs. self-inclusive anthems.</item>
///   <item><c>extraPredicate?.Invoke(permanent) ?? true</c> — optional
///     refinement (e.g. "noncreature permanents you control" or "Equipment
///     you control").</item>
/// </list>
/// </para>
///
/// <para>Layer placement: P/T modifications (<see cref="Layer.PT_Modify"/>)
/// for the +P/+T bonus, granted keywords applied within the same layer
/// for v1 simplicity. This matches <see cref="LordStaticEffect"/>'s MVP
/// shape — the layer system's split-layer pipeline (6 / 7c) is a separate
/// follow-up across every lord-shaped effect.</para>
///
/// <para>For non-creature permanents, <see cref="Apply(PermanentCharacteristics)"/>
/// adds granted keywords directly to the permanent's working set; the
/// P/T fields are creature-only so the bonuses are silently dropped on the
/// non-creature path (CR 613.1f — keyword grants apply to permanents
/// regardless of type, P/T modifications only meaningfully attach to
/// objects with P/T).</para>
/// </summary>
public sealed class ControllerPermanentAnthemEffect : ContinuousEffect
{
    private readonly ICard _source;
    private readonly int _powerBonus;
    private readonly int _toughnessBonus;
    private readonly IReadOnlyList<string> _grantedKeywords;
    private readonly bool _includeSelf;
    private readonly Func<ICard, bool>? _extraPredicate;

    public ControllerPermanentAnthemEffect(
        ICard source,
        int powerBonus = 0,
        int toughnessBonus = 0,
        IEnumerable<string>? grantedKeywords = null,
        bool includeSelf = false,
        Func<ICard, bool>? extraPredicate = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _powerBonus = powerBonus;
        _toughnessBonus = toughnessBonus;
        _grantedKeywords = (grantedKeywords ?? Array.Empty<string>()).ToList();
        _includeSelf = includeSelf;
        _extraPredicate = extraPredicate;
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the permanent generating this continuous effect,
    /// when the source is itself a permanent. Null for non-permanent sources
    /// (emblem-style or test-only callers).</summary>
    public override Permanent? Source => _source as Permanent;

    /// <summary>Active iff the source permanent is currently on the
    /// battlefield. Matches <see cref="LordStaticEffect.IsActive"/>.</summary>
    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => AppliesToCore(creature);

    public override bool AppliesTo(Permanent permanent) => AppliesToCore(permanent);

    private bool AppliesToCore(ICard card)
    {
        if (card is not Permanent perm) return false;
        if (perm.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;

        if (!ReferenceEquals(perm.Controller, _source.Controller)) return false;
        if (!_includeSelf && ReferenceEquals(perm, _source)) return false;
        if (_extraPredicate != null && !_extraPredicate(perm)) return false;

        return true;
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _powerBonus;
        chars.Toughness += _toughnessBonus;
        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }

    /// <summary>
    /// CR 613.1f — keyword grants on non-creature permanents. P/T bonuses
    /// are creature-only and silently dropped here; if the working set is
    /// in fact a <see cref="CreatureCharacteristics"/> we fall through to
    /// the base dispatch so the creature overload runs (and applies both
    /// P/T and keywords).
    /// </summary>
    public override void Apply(PermanentCharacteristics chars)
    {
        if (chars is CreatureCharacteristics cc)
        {
            Apply(cc);
            return;
        }

        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }
}
