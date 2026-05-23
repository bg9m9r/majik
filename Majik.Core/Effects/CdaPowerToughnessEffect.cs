using Majik.Core.Cards;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.2 Layer 7a — characteristic-defining P/T. Evaluators are
/// invoked every Compute, so dynamic inputs (graveyards, permanent
/// counts, etc.) read at compute time. Unlike Layer 7b, 7a SETS the
/// base P/T; subsequent 7c pumps add on top.
///
/// A characteristic-defining ability (CR 604.3) is an inherent property
/// of the card itself that defines its printed P/T dynamically. Examples
/// include Tarmogoyf ("Tarmogoyf's power is equal to the number of card
/// types among cards in all graveyards…") and Mortivore. The CDA always
/// applies regardless of whether the card is on the battlefield in
/// principle, but because <see cref="ContinuousEffectsService"/> only
/// runs <see cref="Apply(CreatureCharacteristics)"/> against battlefield
/// permanents in practice, we gate <see cref="IsActive"/> on the source
/// being on the battlefield. Off-battlefield P/T for CDAs is a deeper
/// concern (see CR 208.2c) and is deferred.
///
/// Layer 7c counter-postlude (CR 613.7 / <see cref="ContinuousEffectsService.Compute(Permanent)"/>)
/// runs AFTER all layer-7 effects, so +1/+1 / -1/-1 counters stack on
/// top of the CDA-defined base. Other 7c pump effects (anthems, Giant
/// Growth) also stack on top, since 7a runs before 7c.
/// </summary>
public sealed class CdaPowerToughnessEffect : ContinuousEffect
{
    private readonly Permanent _source;
    private readonly Func<Permanent, int> _powerOf;
    private readonly Func<Permanent, int> _toughnessOf;

    public CdaPowerToughnessEffect(
        Permanent source,
        Func<Permanent, int> powerOf,
        Func<Permanent, int> toughnessOf)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _powerOf = powerOf ?? throw new ArgumentNullException(nameof(powerOf));
        _toughnessOf = toughnessOf ?? throw new ArgumentNullException(nameof(toughnessOf));
    }

    public override Layer Layer => Layer.PT_Cda;
    public override Permanent? Source => _source;
    public override bool IsActive() => _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;
    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = _powerOf(_source);
        chars.Toughness = _toughnessOf(_source);
    }
}
