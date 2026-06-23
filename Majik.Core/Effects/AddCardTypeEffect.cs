using Majik.Core.Cards;
using Majik.Core.Cards.Types;

namespace Majik.Core.Effects;

/// <summary>
/// CR 613.1d — Layer 4 (type-changing) continuous effect that <em>adds</em> a
/// card <see cref="CardType"/> to a permanent in addition to its other types,
/// without removing any. This is the card-type analogue of
/// <see cref="AddSubtypeEffect"/> (which adds a subtype).
///
/// <para>Canonical consumer: Phyrexian Metamorph's
/// "except it's an artifact in addition to its other types" (CR 706.9c /
/// 613.1d). When Metamorph enters as a copy of a noncreature/non-artifact
/// permanent, the copied characteristics overwrite its type line (Layer 1);
/// this Layer-4 effect then re-adds <see cref="CardType.Artifact"/> on top so
/// the copy is always an Artifact regardless of what it copied.</para>
///
/// Effect is source-anchored: it applies only while the target is on the
/// battlefield, so it ends automatically when the target leaves play.
/// </summary>
public sealed class AddCardTypeEffect : ContinuousEffect
{
    private readonly Permanent _target;
    private readonly bool _expiresAtEndOfTurn;

    /// <summary>The card type unioned onto the target's effective type set.</summary>
    public CardType CardType { get; }

    /// <param name="expiresAtEndOfTurn">When true, the effect is dropped at the
    /// cleanup step (CR 514.2). Defaults to false (lasts while the target is on
    /// the battlefield — Phyrexian Metamorph's permanent "in addition" rider).
    /// Set true for an until-end-of-turn "in addition" rider paired with an
    /// until-EOT copy (Saheeli, Sublime Artificer's −2; CR 707.9b).</param>
    public AddCardTypeEffect(Permanent target, CardType cardType, bool expiresAtEndOfTurn = false)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        CardType = cardType;
        _expiresAtEndOfTurn = expiresAtEndOfTurn;
    }

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool ExpiresAtEndOfTurn => _expiresAtEndOfTurn;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);

    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        // CR 613.1d — ADD unions onto the seeded/Layer-1-copied type set.
        chars.Types.Add(CardType);
    }
}
