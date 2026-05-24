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
///
/// <para>Set <c>opponentsOnly: true</c> to flip the controller filter so
/// the effect applies to matching creatures controlled by anyone OTHER
/// than the source's controller — used by Plague Engineer ("Creatures of
/// the chosen type your opponents control get -1/-1.") and similar
/// debuff-the-opponent statics. With opponentsOnly the source itself is
/// always excluded regardless of <c>includeSelf</c>.</para>
///
/// <para>Set <c>allPlayers: true</c> to bypass the controller filter
/// entirely so the effect applies to matching creatures controlled by
/// ANY player — used by Engineered Plague ("All creatures of the chosen
/// type get -1/-1."). When allPlayers is true, opponentsOnly is
/// ignored.</para>
/// </summary>
public sealed class LordStaticEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly CardSubtype _subtype;
    private readonly int _power;
    private readonly int _toughness;
    private readonly IReadOnlyList<string> _grantedKeywords;
    private readonly bool _includeSelf;
    private readonly bool _opponentsOnly;
    private readonly bool _allPlayers;

    public LordStaticEffect(
        Permanent source,
        CardSubtype matchingSubtype,
        int power = 1,
        int toughness = 1,
        IReadOnlyList<string>? grantedKeywords = null,
        bool includeSelf = false,
        bool opponentsOnly = false,
        bool allPlayers = false)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _subtype = matchingSubtype;
        _power = power;
        _toughness = toughness;
        _grantedKeywords = grantedKeywords ?? Array.Empty<string>();
        _includeSelf = includeSelf;
        _opponentsOnly = opponentsOnly;
        _allPlayers = allPlayers;
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the lord permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;
        if (_allPlayers)
        {
            // No controller filter — effect applies to ALL creatures of the
            // matching subtype regardless of controller. Used by Engineered
            // Plague ("All creatures of the chosen type get -1/-1.").
            return creature.HasSubtype(_subtype);
        }
        var sameController = ReferenceEquals(creature.Controller, _source.Controller);
        if (_opponentsOnly)
        {
            // CR 109.5 — "opponents control" excludes everything the
            // source's controller controls (including the source itself).
            if (sameController) return false;
        }
        else
        {
            if (!sameController) return false;
            if (!_includeSelf && ReferenceEquals(creature, _source)) return false;
        }
        return creature.HasSubtype(_subtype);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }
}
