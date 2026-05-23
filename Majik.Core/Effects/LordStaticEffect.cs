using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// Generic "Lord" static effect — "Other creatures of TYPE you control
/// get +P/+T (and gain KEYWORDS)." Layer 7c for P/T, Layer 6 for granted
/// keywords; this MVP places both in 7c via direct chars mutation.
///
/// While source is on the battlefield, every matching creature controlled
/// by the source's controller (excluding the source itself) receives the
/// bonus.
/// </summary>
public sealed class LordStaticEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly CardSubtype _subtype;
    private readonly int _power;
    private readonly int _toughness;
    private readonly IReadOnlyList<string> _grantedKeywords;
    private readonly bool _includeSelf;

    public LordStaticEffect(
        Permanent source,
        CardSubtype matchingSubtype,
        int power = 1,
        int toughness = 1,
        IReadOnlyList<string>? grantedKeywords = null,
        bool includeSelf = false)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _subtype = matchingSubtype;
        _power = power;
        _toughness = toughness;
        _grantedKeywords = grantedKeywords ?? Array.Empty<string>();
        _includeSelf = includeSelf;
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the lord permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;
        if (!ReferenceEquals(creature.Controller, _source.Controller)) return false;
        if (!_includeSelf && ReferenceEquals(creature, _source)) return false;
        return creature.HasSubtype(_subtype);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }
}
