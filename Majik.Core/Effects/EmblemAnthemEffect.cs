using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.Effects;

/// <summary>
/// CR 114 / CR 613.7c — an EMBLEM-sourced static anthem: "[Subtype]s you
/// control get +P/+T (and gain KEYWORDS)" (e.g. Kaito's emblem "Ninjas you
/// control get +1/+1", Liliana, the Last Hope's "Zombies you control get
/// +2/+2"). The continuous boost lands in Layer 7c via direct
/// <see cref="CreatureCharacteristics"/> mutation, mirroring
/// <see cref="LordStaticEffect"/>.
///
/// <para>Unlike <see cref="LordStaticEffect"/>, the source is NOT a
/// <see cref="Permanent"/> on the battlefield — an emblem (CR 114) has no
/// characteristics and no zone; it lives in the COMMAND zone for the rest of
/// the game. So <see cref="IsActive"/> is unconditionally <c>true</c> (the
/// emblem never leaves), and the controller scope is a <see cref="Player"/>
/// reference captured at emblem creation rather than a permanent's live
/// <c>Controller</c>. The effect is registered into the per-game
/// <see cref="ContinuousEffectsService"/> at emblem-creation time so the
/// anthem is LIVE in production (CR 613.7c), not merely structural.</para>
///
/// <para>Pass <paramref name="matchingSubtype"/> as <c>null</c> to apply to
/// ALL creatures the controller controls (a typeless "creatures you control
/// get +N/+N" emblem). The subtype membership read consults the candidate's
/// PRINTED subtype (<see cref="Creature.HasSubtype"/>), mirroring
/// <see cref="LordStaticEffect"/> — the effective-subtype read would re-enter
/// the layer pipeline mid-pass and recurse.</para>
/// </summary>
public sealed class EmblemAnthemEffect : ContinuousEffect
{
    private readonly Player _controller;
    private readonly CardSubtype? _subtype;
    private readonly int _power;
    private readonly int _toughness;
    private readonly IReadOnlyList<string> _grantedKeywords;

    /// <summary>
    /// Construct an emblem anthem.
    /// </summary>
    /// <param name="controller">The player who controls the emblem — the
    /// "you" in "[Subtype]s you control" (CR 114 / 109.5).</param>
    /// <param name="matchingSubtype">The creature subtype the anthem boosts,
    /// or <c>null</c> for an untyped "creatures you control" anthem.</param>
    /// <param name="power">Power bonus (CR 613.7c). Default +1.</param>
    /// <param name="toughness">Toughness bonus. Default +1.</param>
    /// <param name="grantedKeywords">Keywords the anthem grants (e.g.
    /// "Zombies you control get +2/+2 and gain menace"). Default none.</param>
    public EmblemAnthemEffect(
        Player controller,
        CardSubtype? matchingSubtype,
        int power = 1,
        int toughness = 1,
        IReadOnlyList<string>? grantedKeywords = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _subtype = matchingSubtype;
        _power = power;
        _toughness = toughness;
        _grantedKeywords = grantedKeywords ?? Array.Empty<string>();
    }

    /// <summary>The emblem's controlling player ("you").</summary>
    public Player Controller => _controller;

    /// <summary>The boosted subtype, or null for a typeless creature anthem.</summary>
    public CardSubtype? Subtype => _subtype;

    public override Layer Layer => Layer.PT_Modify;

    // CR 114 — an emblem lives in the command zone for the rest of the game,
    // so its static anthem is always active; there is no battlefield-source
    // zone gate (contrast LordStaticEffect.IsActive).
    public override bool IsActive() => true;

    public override bool ExpiresAtEndOfTurn => false;

    // No Source — an emblem is not a Permanent (CR 114). The layer service's
    // Humility-class source-suppression (keyed on a battlefield Permanent
    // source) therefore never drops an emblem anthem, which is correct: an
    // emblem's abilities can't be removed by Layer-6 ability-stripping that
    // targets permanents.

    public override bool AppliesTo(Creature creature)
    {
        if (creature.Zone != Majik.Core.Zones.ZoneType.Battlefield) return false;
        // CR 109.5 — "you control" scopes to the emblem's controller.
        if (!ReferenceEquals(creature.Controller, _controller)) return false;
        if (_subtype == null) return true;
        // CR 613.7c — membership reads the PRINTED subtype (via HasSubtype),
        // mirroring LordStaticEffect's subtype gate. The effective-subtype
        // read (GetEffectiveSubtypes → Compute) is deliberately NOT used here:
        // it would re-enter Compute mid-pass and recurse (the same Layer-7c
        // anthem-membership re-entrancy LordStaticEffect avoids by reading
        // printed subtypes). Animated/copied-subtype membership is the same
        // boundary as LordStaticEffect — a follow-up if a card needs it.
        return creature.HasSubtype(_subtype.Value);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }
}
