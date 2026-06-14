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
    private readonly bool _expiresAtEndOfTurn;

    public CdaPowerToughnessEffect(
        Permanent source,
        Func<Permanent, int> powerOf,
        Func<Permanent, int> toughnessOf,
        bool expiresAtEndOfTurn = false)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _powerOf = powerOf ?? throw new ArgumentNullException(nameof(powerOf));
        _toughnessOf = toughnessOf ?? throw new ArgumentNullException(nameof(toughnessOf));
        _expiresAtEndOfTurn = expiresAtEndOfTurn;
    }

    public override Layer Layer => Layer.PT_Cda;
    public override Permanent? Source => _source;
    public override bool IsActive() => _source.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    /// <summary>
    /// True when the CDA was granted by a duration-bounded animate ability
    /// (CR 514.2 — Svogthos's <c>{3}{B}{G}: Until end of turn, this land
    /// becomes a … Plant Zombie creature with "&lt;CDA&gt;"</c>). A printed CDA
    /// on a real creature (Tarmogoyf) is intrinsic and never expires; that path
    /// passes the default <c>false</c>.
    /// </summary>
    public override bool ExpiresAtEndOfTurn => _expiresAtEndOfTurn;
    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _source);

    // CR 613.1c — an animated NON-creature C# instance (a Land becoming a
    // creature via Svogthos's animate ability) is upgraded to a creature row by
    // ContinuousEffectsService.Compute, which dispatches via AppliesTo(Permanent).
    // The base override gates on `permanent is Creature`, which a Land instance
    // is NOT, so the CDA P/T would never surface on an animated land without
    // this reference-equality override.
    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        chars.Power = _powerOf(_source);
        chars.Toughness = _toughnessOf(_source);
    }
}
