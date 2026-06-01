using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;

namespace Majik.Core.Rules;

/// <summary>
/// CR 115 — targeting legality checks. Today only enforces hexproof (CR
/// 702.11 — can't be targeted by opponents) and shroud (CR 702.18 — can't
/// be targeted by anyone). Future expansion: protection from source's
/// colour / type, "can't be the target of spells your opponents control"
/// (Aegis-style), planeswalker target redirection (CR 113.3a).
/// </summary>
public static class TargetLegality
{
    /// <summary>
    /// True if <paramref name="target"/> can be the target of a spell/ability
    /// controlled by <paramref name="caster"/>.
    /// </summary>
    public static bool CanBeTargetedBy(Creature target, Player caster)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (caster == null) throw new ArgumentNullException(nameof(caster));

        if (Has(target, "Shroud")) return false;
        if (Has(target, "Hexproof") && !ReferenceEquals(target.Controller, caster))
            return false;
        return true;
    }

    /// <summary>
    /// CR 702.16e — protection from <em>colour</em> prevents targeting from
    /// any source matching that colour. Use this overload when the caller
    /// has the source card available (e.g. a spell being cast).
    ///
    /// CR 105.3 / 613.1e — the source's colour can be changed by a Layer-5
    /// colour-changing effect (Painter's Servant making a spell red, an
    /// animated land "is all colours"). The protection check therefore reads
    /// the source's <em>effective</em> colour via
    /// <see cref="Permanent.GetEffectiveColors"/> when the source is a
    /// permanent; a non-permanent source with no Layer-5 colour effect falls
    /// back to its printed/static colour. Mirrors the combat-protection path in
    /// <see cref="Majik.Core.Combat.CombatFlow"/>.
    /// </summary>
    public static bool CanBeTargetedBy(Creature target, ICard source, Player caster)
    {
        if (!CanBeTargetedBy(target, caster)) return false;
        if (source == null) return true;

        foreach (var c in EffectiveColorsOf(source))
        {
            if (Protection.HasProtectionFromColor(target, c)) return false;
        }
        return true;
    }

    /// <summary>
    /// CR 105.3 / 613.1e — the source's effective colour set: the Layer-5
    /// colour-changing pass via <see cref="Permanent.GetEffectiveColors"/> when
    /// the source is a permanent, otherwise the printed/static colour (a
    /// non-permanent source carries no Layer-5 colour effect).
    /// </summary>
    private static IReadOnlySet<Majik.Core.ValueObjects.ManaColor> EffectiveColorsOf(ICard source) =>
        source is Permanent perm
            ? perm.GetEffectiveColors()
            : Majik.Core.Cards.CardColors.GetColors(source);

    private static bool Has(Creature c, string kw) =>
        c.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, kw, StringComparison.OrdinalIgnoreCase))
        || (c.ActiveEffects?.Compute(c).Keywords.Contains(kw) ?? false);
}
