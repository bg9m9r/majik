using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Zones;

namespace Majik.Core.Targeting;

/// <summary>
/// CR 115.4 / 711 / 306.7 — classify a permanent as a damage target by its
/// EFFECTIVE (layer-computed) characteristics rather than its printed C#
/// instance type.
///
/// <para>
/// A creature-front transform DFC flipped to its planeswalker BACK face (Ral,
/// Monsoon Mage // Ral, Leyline Prodigy) stays a <see cref="Creature"/> C#
/// instance — re-classing the runtime object is the trap the
/// planeswalker-back-reclass deferral calls out. Instead, the back face carries
/// a transient loyalty body and computes the Planeswalker type through the layer
/// system. The damage-APPLICATION half already honours this:
/// <see cref="Primitives.Fx.DealDamageAny(object,int)"/> routes such a permanent
/// to <see cref="Permanent.RemoveTransientLoyalty"/> via
/// <see cref="Permanent.IsEffectivePlaneswalker"/>.
/// </para>
///
/// <para>
/// This helper is the targeting-OFFERING half: the candidate gatherers that
/// enumerate "any target" / "target creature" / "target creature or
/// planeswalker" damage targets must classify a flipped DFC by
/// <see cref="Permanent.IsEffectivelyCreature"/> /
/// <see cref="Permanent.IsEffectivePlaneswalker"/> — NOT by the lingering
/// printed <see cref="Card.HasType"/>(<see cref="CardType.Creature"/>) flag,
/// which still reads true on a flipped creature-front DFC. Otherwise a "target
/// creature" spell would wrongly offer a planeswalker back face, and an "any
/// target" spell would mislabel it as a creature.
/// </para>
/// </summary>
public static class DamageTargeting
{
    /// <summary>
    /// True when <paramref name="card"/> is a battlefield permanent that is
    /// effectively a creature (CR 613.1c) — a real creature, an animated
    /// land/artifact, but NOT a creature-front DFC flipped to a non-creature
    /// planeswalker back. The legal target of "target creature" damage.
    /// </summary>
    public static bool IsCreatureDamageTarget(ICard card) =>
        card is Permanent p
        && p.Zone == ZoneType.Battlefield
        && p.IsEffectivelyCreature();

    /// <summary>
    /// True when <paramref name="card"/> is a battlefield permanent that carries
    /// a working loyalty body (CR 711 / 306.5b) — a real planeswalker OR a
    /// creature-front DFC flipped to its planeswalker back face. The legal
    /// target of "target planeswalker" damage.
    /// </summary>
    public static bool IsPlaneswalkerDamageTarget(ICard card) =>
        card is Permanent p
        && p.Zone == ZoneType.Battlefield
        && p.IsEffectivePlaneswalker();

    /// <summary>
    /// True when <paramref name="card"/> is a legal "any target" permanent
    /// damage candidate (CR 115.4) — effectively a creature OR an effective
    /// planeswalker. ("Any target" also covers players, handled separately by
    /// callers that add the player list.)
    /// </summary>
    public static bool IsAnyDamageTarget(ICard card) =>
        IsCreatureDamageTarget(card) || IsPlaneswalkerDamageTarget(card);
}
