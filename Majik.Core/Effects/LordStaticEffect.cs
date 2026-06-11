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
/// <para>Pass <c>matchingKeyword</c> (the <c>(Permanent, string, ...)</c>
/// constructor) for the KEYWORD-GATED anthem shape — "Other creatures you
/// control with [keyword] get +N/+N" (CR 613.4). The membership filter then
/// gates on the candidate's EFFECTIVE keyword (post-Layer-6, so a creature
/// GRANTED the keyword counts — CR 613.8) via
/// <see cref="Creature.HasEffectiveKeyword"/>. Closes Empyrean Eagle and the
/// flying / landwalk / etc. keyword-anthem cluster. When both a subtype and a
/// keyword are supplied (canonical constructor) they are ANDed.</para>
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
///
/// <para>Pass <c>matchingSubtype: null</c> to skip the subtype filter
/// entirely — the effect applies to ALL creature permanents in the
/// relevant controller scope. Used by Waker of Waves ("Creatures your
/// opponents control get -1/-0.") where no creature type restriction
/// exists. CR 613.7c — scope is still governed by opponentsOnly /
/// allPlayers / includeSelf flags.</para>
///
/// <para>Set <c>tokensOnly: true</c> to additionally gate on the creature
/// being a token (CR 111). Combined with <c>matchingSubtype: null</c> this
/// is the "Creature tokens you control get +1/+1 (and have KEYWORD)" shape
/// used by Intangible Virtue. The token gate is ANDed with the subtype /
/// controller filters; it is orthogonal to them.</para>
/// </summary>
public sealed class LordStaticEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly CardSubtype? _subtype;
    private readonly string? _matchingKeyword;
    private readonly int _power;
    private readonly int _toughness;
    private readonly IReadOnlyList<string> _grantedKeywords;
    private readonly bool _includeSelf;
    private readonly bool _opponentsOnly;
    private readonly bool _allPlayers;
    private readonly bool _tokensOnly;

    /// <summary>
    /// Construct with a specific creature-type filter.
    /// </summary>
    public LordStaticEffect(
        Permanent source,
        CardSubtype matchingSubtype,
        int power = 1,
        int toughness = 1,
        IReadOnlyList<string>? grantedKeywords = null,
        bool includeSelf = false,
        bool opponentsOnly = false,
        bool allPlayers = false,
        bool tokensOnly = false)
        : this(source, (CardSubtype?)matchingSubtype, matchingKeyword: null, power, toughness,
               grantedKeywords, includeSelf, opponentsOnly, allPlayers, tokensOnly)
    {
    }

    /// <summary>
    /// Construct the KEYWORD-GATED anthem variant — "Other creatures you
    /// control with [keyword] get +N/+N" (CR 613.4 / 613.7c). The affected
    /// set is filtered by the candidate creature's EFFECTIVE keyword
    /// (post-Layer-6, so a granted keyword counts — CR 613.8), via
    /// <see cref="Creature.HasEffectiveKeyword"/>. Closes Empyrean Eagle and
    /// the keyword-anthem cluster (flying / landwalk / etc. lords gated by a
    /// keyword rather than a creature subtype). No subtype gate is applied
    /// (<paramref name="matchingKeyword"/> is the only membership filter
    /// beyond controller scope and the "other" clause); pass the
    /// canonical constructor directly to AND a subtype with a keyword.
    /// </summary>
    public LordStaticEffect(
        Permanent source,
        string matchingKeyword,
        int power = 1,
        int toughness = 1,
        IReadOnlyList<string>? grantedKeywords = null,
        bool includeSelf = false,
        bool opponentsOnly = false,
        bool allPlayers = false,
        bool tokensOnly = false)
        : this(source, matchingSubtype: null,
               matchingKeyword: string.IsNullOrWhiteSpace(matchingKeyword)
                   ? throw new ArgumentException("Keyword required", nameof(matchingKeyword))
                   : matchingKeyword,
               power, toughness, grantedKeywords, includeSelf, opponentsOnly, allPlayers, tokensOnly)
    {
    }

    /// <summary>
    /// Canonical constructor. Pass <paramref name="matchingSubtype"/> as
    /// <c>null</c> to skip the subtype gate, and/or
    /// <paramref name="matchingKeyword"/> as <c>null</c> to skip the
    /// effective-keyword gate. When both are set they are ANDed; when both
    /// are null the effect applies to ALL creatures in the relevant
    /// controller scope.
    /// </summary>
    public LordStaticEffect(
        Permanent source,
        CardSubtype? matchingSubtype,
        string? matchingKeyword = null,
        int power = 1,
        int toughness = 1,
        IReadOnlyList<string>? grantedKeywords = null,
        bool includeSelf = false,
        bool opponentsOnly = false,
        bool allPlayers = false,
        bool tokensOnly = false)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _subtype = matchingSubtype;
        _matchingKeyword = matchingKeyword;
        _power = power;
        _toughness = toughness;
        _grantedKeywords = grantedKeywords ?? Array.Empty<string>();
        _includeSelf = includeSelf;
        _opponentsOnly = opponentsOnly;
        _allPlayers = allPlayers;
        _tokensOnly = tokensOnly;
    }

    public override Layer Layer => Layer.PT_Modify;

    /// <summary>CR 613.1g — the lord permanent generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;
        // CR 111 — optional token gate ("Creature tokens you control ...").
        // ANDed with every controller / subtype branch below; orthogonal to
        // them. Default false preserves the all-creatures behaviour.
        if (_tokensOnly && !creature.IsToken) return false;
        if (_allPlayers)
        {
            // No controller filter — effect applies to ALL creatures of the
            // matching subtype regardless of controller. Used by Engineered
            // Plague ("All creatures of the chosen type get -1/-1.").
            // includeSelf is still honoured: Lord of Atlantis says "Other
            // Merfolk" (allPlayers: true, includeSelf: false) so it must
            // exclude itself from its own buff.
            if (!_includeSelf && ReferenceEquals(creature, _source)) return false;
            return MatchesMembership(creature);
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
        return MatchesMembership(creature);
    }

    /// <summary>
    /// AND of the optional subtype gate and the optional effective-keyword
    /// gate. <c>_subtype == null</c> → no type restriction; <c>_matchingKeyword
    /// == null</c> → no keyword restriction. CR 613.8 — the keyword gate reads
    /// the candidate's POST-Layer-6 keyword set
    /// (<see cref="Creature.HasEffectiveKeyword"/>), so a creature GRANTED the
    /// keyword (e.g. flying) qualifies for this Layer-7c boost.
    /// </summary>
    private bool MatchesMembership(Creature creature)
    {
        if (_subtype != null && !creature.HasSubtype(_subtype.Value)) return false;
        if (_matchingKeyword != null && !creature.HasEffectiveKeyword(_matchingKeyword)) return false;
        return true;
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="LordStaticEffect"/> bound to the
    /// <paramref name="clonedSource"/> permanent for the search-sandbox clone.  All
    /// value-type configuration fields (_subtype, _matchingKeyword, _power, _toughness,
    /// _grantedKeywords, _includeSelf, _opponentsOnly, _allPlayers, _tokensOnly) are
    /// copied from <c>this</c> so the reconstructed effect is behaviourally identical to
    /// the live one within the clone universe.  The <paramref name="clonedPlayers"/>
    /// resolver is accepted but unused — <see cref="LordStaticEffect"/> derives its player
    /// scope from <c>Source.Controller</c>, which is correctly wired on the cloned
    /// permanent, so no external resolver is required.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => new LordStaticEffect(
            source:          clonedSource,
            matchingSubtype: _subtype,
            matchingKeyword: _matchingKeyword,
            power:           _power,
            toughness:       _toughness,
            grantedKeywords: _grantedKeywords,
            includeSelf:     _includeSelf,
            opponentsOnly:   _opponentsOnly,
            allPlayers:      _allPlayers,
            tokensOnly:      _tokensOnly);
}
