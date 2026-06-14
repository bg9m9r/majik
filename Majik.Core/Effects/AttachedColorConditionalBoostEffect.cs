using Majik.Core.Cards;
using Majik.Core.ValueObjects;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613 — "As long as enchanted creature is &lt;colour&gt;, it gets +P/+T
/// (and gains KEYWORDS)." A colour-gated variant of
/// <see cref="AttachedBoostEffect"/>: the boost applies only while the
/// permanent this effect is attached to (read dynamically from
/// <see cref="Permanent.AttachedTo"/>) currently HAS the required colour
/// (CR 105.3 / 613.1e — effective colour after the Layer-5 colour pass).
///
/// <para>Models Steel of the Godhead's two colour clauses:
///   - white clause → +1/+1 and lifelink (CR 702.15);
///   - blue clause  → +1/+1 (the "can't be blocked" rider is a separate
///     <see cref="CombatRestrictionEffect"/> — restrictions aren't baked
///     into the characteristics working set).</para>
///
/// <para>The colour gate reads the enchanted creature's POST-Layer-5 colour
/// via <see cref="ContinuousEffectsService.EffectiveColors"/> — NOT
/// <see cref="Permanent.GetEffectiveColors"/>, which runs the full pipeline
/// and would re-enter <see cref="ContinuousEffectsService.Compute"/> for the
/// same creature and recurse back through this effect's
/// <see cref="AppliesTo(Creature)"/>. Stopping at Layer 5 respects the
/// CR 613.8 dependency (this Layer-6/7c effect depends on the Layer-5 colour
/// pass) without the recursion, so a creature TURNED white/blue by another
/// effect correctly gains the boost, and one whose colour is removed loses
/// it.</para>
///
/// <para>P/T lands in Layer 7c (<see cref="Layer.PT_Modify"/>); the granted
/// keyword (lifelink) rides the same effect's <see cref="Apply"/> into the
/// keyword set (Layer 6 semantically — same single-effect shape as
/// <see cref="AttachedBoostEffect"/>). The boost transfers automatically if
/// the aura is re-attached because the source's <see cref="Permanent.AttachedTo"/>
/// is read on every layer pass.</para>
/// </summary>
public sealed class AttachedColorConditionalBoostEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly ManaColor _requiredColor;
    private readonly int _power;
    private readonly int _toughness;
    private readonly IReadOnlyList<string> _grantedKeywords;
    private readonly Layer _layer;

    /// <param name="layer">CR 613 — the layer this effect applies in. P/T
    /// bumps belong in <see cref="Layer.PT_Modify"/> (7c, the default);
    /// keyword grants belong in <see cref="Layer.Abilities"/> (6) so they
    /// surface through <see cref="ContinuousEffectsService.EffectiveKeywords"/>
    /// (which stops at Layer 6). A factory with BOTH a P/T bump and a keyword
    /// grant registers two of these effects, one per layer.</param>
    public AttachedColorConditionalBoostEffect(
        Permanent source,
        ManaColor requiredColor,
        int power = 0,
        int toughness = 0,
        IReadOnlyList<string>? grantedKeywords = null,
        Layer layer = Effects.Layer.PT_Modify)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _requiredColor = requiredColor;
        _power = power;
        _toughness = toughness;
        _grantedKeywords = grantedKeywords ?? Array.Empty<string>();
        _layer = layer;
    }

    public override Layer Layer => _layer;

    /// <summary>CR 613.1g — the Aura generating this effect.</summary>
    public override Permanent? Source => _source;

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield
        && _source.AttachedTo != null
        && _source.AttachedTo.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature)
    {
        if (!ReferenceEquals(_source.AttachedTo, creature)) return false;
        // CR 105.3 / 613.1e — gate on the enchanted creature's EFFECTIVE
        // colour (post-Layer-5). EffectiveColors stops at Layer 5, so reading
        // it from this Layer-6/7c effect's AppliesTo is re-entrancy-safe.
        var colors = creature.ActiveEffects?.EffectiveColors(creature)
                     ?? Majik.Core.Cards.CardColors.GetColors(creature);
        return colors.Contains(_requiredColor);
    }

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power += _power;
        chars.Toughness += _toughness;
        foreach (var kw in _grantedKeywords) chars.Keywords.Add(kw);
    }

    /// <summary>
    /// Sim-only: reconstruct an identical effect bound to
    /// <paramref name="clonedSource"/>. All configuration is value-type /
    /// immutable and the colour gate reads the cloned source's
    /// <see cref="Permanent.AttachedTo"/> (remapped by the cloner), so the
    /// reconstructed effect is behaviourally identical in the sandbox.
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        // preserves: _source→clonedSource, _requiredColor, _power, _toughness, _grantedKeywords, _layer
        => new AttachedColorConditionalBoostEffect(
            source:          clonedSource,
            requiredColor:   _requiredColor,
            power:           _power,
            toughness:       _toughness,
            grantedKeywords: _grantedKeywords,
            layer:           _layer);
}
