using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// Karn, the Great Creator — +1 ability resolution:
/// "Until your next turn, up to one target noncreature artifact becomes
/// an artifact creature with power and toughness each equal to its
/// mana value."
///
/// CR 613.1c — Layer 4 (type-changing). Adds <see cref="CardType.Creature"/>
/// to the target permanent's effective types for the duration. The Layer
/// 7b "set base P/T" half of the +1 is paired with this via
/// <see cref="BecomesPTEffect"/> in <see cref="Majik.Core.CardData.Factories.KarnTheGreatCreatorFactory"/>
/// — the +1 registers BOTH effects together so a future Compute pass
/// sees Creature (Layer 4) before sublayer 7b sets P/T.
///
/// "Until your next turn" is approximated via <see cref="ExpiresAtEndOfTurn"/>
/// (true). The exact "your next turn" boundary requires a controller-keyed
/// duration primitive the engine doesn't yet have; end-of-turn is a
/// shorter-duration upper bound, which is observationally indistinguishable
/// for combat math on the resolving turn. Documented v1 deviation —
/// extending to "until your next untap step" is a follow-up.
///
/// Because <see cref="ContinuousEffectsService.Compute(Permanent)"/>
/// currently seeds <see cref="CreatureCharacteristics"/> only when the
/// permanent's runtime C# type is <see cref="Creature"/>, animating a
/// non-Creature artifact (e.g. Sol Ring) does NOT route through the P/T
/// pipeline. The +1's BecomesPTEffect is still registered for layer-
/// system correctness — its NewPower / NewToughness are inspectable for
/// tests — but the artifact's printed shape lacks P/T fields to display.
/// Fully supporting "noncreature becomes creature" combat math is a
/// follow-up that requires Compute(Permanent) to upgrade its
/// characteristics row when Layer 4 grants Creature type.
/// </summary>
public sealed class KarnAnimateArtifactEffect : ContinuousEffect
{
    private readonly Permanent _target;

    public KarnAnimateArtifactEffect(Permanent target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>The permanent being animated.</summary>
    public Permanent Target => _target;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool AppliesTo(Creature creature) => AppliesTo((Permanent)creature);

    public override bool AppliesTo(Permanent permanent) =>
        ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        chars.Types.Add(CardType.Creature);
    }
}
