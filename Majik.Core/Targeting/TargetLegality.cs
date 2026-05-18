using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Targeting;

/// <summary>
/// CR 115 + 702 — given a <see cref="TargetSpec"/> and the current
/// game state, enumerate legal targets and check single-candidate
/// legality (used at cast time AND on resolution per CR 608.2b).
///
/// Honors untargetability keywords on creatures:
///   - Hexproof — can't be targeted by spells/abilities your opponents control
///   - Shroud — can't be targeted at all
///   - Protection from X — can't be targeted by spells/abilities of X
/// </summary>
public static class TargetLegality
{
    /// <summary>
    /// True if <paramref name="candidate"/> is a legal target for the
    /// given spec, cast by <paramref name="caster"/>.
    /// </summary>
    public static bool IsLegal(
        TargetSpec spec,
        object candidate,
        Player caster,
        string? sourceColor = null)
    {
        if (!spec.Matches(candidate)) return false;

        if (candidate is Creature creature)
        {
            // Must still be on the battlefield (CR 115.5).
            if (creature.Zone != ZoneType.Battlefield) return false;

            // Shroud — no spell or ability may target this creature.
            if (HasKeyword(creature, "Shroud")) return false;

            // Hexproof — opponents' spells/abilities can't target.
            if (HasKeyword(creature, "Hexproof")
                && !ReferenceEquals(creature.Controller, caster))
                return false;

            // Protection from COLOR (basic case).
            if (sourceColor != null
                && HasKeyword(creature, $"Protection from {sourceColor}"))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Enumerate every legal candidate currently on the battlefield (for
    /// permanent targets) and every player (for player targets).
    /// </summary>
    public static IEnumerable<object> EnumerateLegal(
        TargetSpec spec,
        Player caster,
        IReadOnlyList<Player> players,
        string? sourceColor = null)
    {
        if (spec.AcceptsPlayers)
        {
            foreach (var p in players)
            {
                if (IsLegal(spec, p, caster, sourceColor)) yield return p;
            }
        }

        foreach (var p in players)
        {
            foreach (var card in p.Zones.Battlefield.GetCards())
            {
                if (IsLegal(spec, card, caster, sourceColor)) yield return card;
            }
        }
    }

    private static bool HasKeyword(Creature c, string keyword)
    {
        if (c.ActiveEffects != null)
        {
            return c.ActiveEffects.Compute(c).Keywords.Contains(keyword);
        }
        return c.Abilities
            .OfType<Majik.Core.Abilities.KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
    }
}
