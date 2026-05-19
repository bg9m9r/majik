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
    /// </summary>
    public static bool CanBeTargetedBy(Creature target, ICard source, Player caster)
    {
        if (!CanBeTargetedBy(target, caster)) return false;
        if (source == null) return true;

        foreach (var c in Majik.Core.Cards.CardColors.GetColors(source))
        {
            if (Protection.HasProtectionFromColor(target, c)) return false;
        }
        return true;
    }

    private static bool Has(Creature c, string kw) =>
        c.Abilities.OfType<KeywordAbility>().Any(k =>
            string.Equals(k.Keyword, kw, StringComparison.OrdinalIgnoreCase))
        || (c.ActiveEffects?.Compute(c).Keywords.Contains(kw) ?? false);
}
