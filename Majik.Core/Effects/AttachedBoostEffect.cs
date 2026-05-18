using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// Generic continuous effect for "while attached, equipped/enchanted
/// creature gets +P/+T and gains keywords." Reads the source's current
/// <see cref="Permanent.AttachedTo"/> dynamically, so re-equipping a
/// sword onto a new creature transfers the bonus automatically.
///
/// Layer 7c for P/T, Layer 6 for granted keywords — this single record
/// fires in both layers (handled by registering twice with different
/// <see cref="Layer"/> values, or by using a sentinel as below — MVP
/// keeps it in 7c only).
/// </summary>
public sealed class AttachedBoostEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly int _power;
    private readonly int _toughness;
    private readonly IReadOnlyList<string> _grantedKeywords;
    private readonly Layer _layer;

    public override Layer Layer => _layer;

    public AttachedBoostEffect(
        Permanent source,
        int power = 0,
        int toughness = 0,
        IReadOnlyList<string>? grantedKeywords = null,
        Layer layer = Effects.Layer.PT_Modify)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _power = power;
        _toughness = toughness;
        _grantedKeywords = grantedKeywords ?? Array.Empty<string>();
        _layer = layer;
    }

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield
        && _source.AttachedTo != null
        && _source.AttachedTo.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) =>
        ReferenceEquals(_source.AttachedTo, creature);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }
}
