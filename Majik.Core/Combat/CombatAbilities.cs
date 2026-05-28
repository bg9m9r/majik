using Majik.Core.Abilities;
using Majik.Core.Cards;

namespace Majik.Core.Combat;

/// <summary>
/// Lookups for combat-relevant evergreen keywords. Currently sourced from
/// <see cref="KeywordAbility"/> markers attached to the creature. A future
/// layer system can plug in granted keywords without changing callers.
/// </summary>
public static class CombatAbilities
{
    public static bool HasFirstStrike(Creature c) => Has(c, "First strike");
    public static bool HasDoubleStrike(Creature c) => Has(c, "Double strike");
    public static bool HasTrample(Creature c) => Has(c, "Trample");
    public static bool HasDeathtouch(Creature c) => Has(c, "Deathtouch");
    public static bool HasVigilance(Creature c) => Has(c, "Vigilance");
    public static bool HasHaste(Creature c) => Has(c, "Haste");
    public static bool HasReach(Creature c) => Has(c, "Reach");
    public static bool HasFlying(Creature c) => Has(c, "Flying");
    public static bool HasLifelink(Creature c) => Has(c, "Lifelink");
    public static bool HasIndestructible(Creature c) => Has(c, "Indestructible");
    public static bool HasMenace(Creature c) => Has(c, "Menace");
    public static bool HasDefender(Creature c) => Has(c, "Defender");

    /// <summary>
    /// CR 509.1b — returns the minimum number of blockers required to
    /// legally block this creature (from a
    /// <see cref="KeywordAbility"/> with keyword
    /// <c>"CantBeBlockedExceptByMinBlockers"</c> and
    /// <see cref="KeywordAbility.Arg"/> = N), or null if no such
    /// restriction exists. Menace is NOT counted here — use
    /// <see cref="HasMenace"/> for the two-or-more check.
    /// </summary>
    public static int? GetMinBlockerRestriction(Creature? c)
    {
        if (c == null) return null;
        var marker = c.Abilities
            .OfType<KeywordAbility>()
            .FirstOrDefault(k => string.Equals(
                k.Keyword,
                "CantBeBlockedExceptByMinBlockers",
                StringComparison.OrdinalIgnoreCase));
        return marker?.Arg;
    }

    public static bool CanBlockFlying(Creature c) => HasFlying(c) || HasReach(c);

    private static bool Has(Creature? creature, string keyword)
    {
        if (creature == null) return false;

        // Layer system source-of-truth, when wired (CR 613).
        if (creature.ActiveEffects != null)
        {
            return creature.ActiveEffects.Compute(creature).Keywords
                .Contains(keyword);
        }

        // Fallback: printed KeywordAbility markers.
        return creature.Abilities
            .OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
    }
}
