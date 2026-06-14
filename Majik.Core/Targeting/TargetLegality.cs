using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Targeting;

/// <summary>
/// CR 115 + 702 — given a <see cref="TargetSpec"/> and the current
/// game state, enumerate legal targets and check single-candidate
/// legality (used at cast time AND on resolution per CR 608.2b).
///
/// Honors untargetability keywords on creatures:
///   - Hexproof — can't be targeted by spells/abilities your opponents control
///   - Hexproof from [colour] (CR 702.11e) — can't be targeted by your
///     opponents' spells/abilities whose source is of that colour
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

        // CR 702.11 / CR 113.5 — player-hexproof. A player with hexproof
        // can't be the target of spells or abilities controlled by
        // opponents (Leyline of Sanctity / True Believer / Aegis of the
        // Gods). Same-controller targeting (self-target Healing Salve,
        // self-mill, etc.) is unaffected.
        if (candidate is Player playerCandidate)
        {
            // CR 702.18 — player-level SHROUD (Solitary Confinement). Unlike
            // hexproof, shroud blocks ALL targeting, INCLUDING the player's
            // own spells and abilities (CR 702.18a — no controller exception).
            if (Majik.Core.Rules.PlayerStaticAbilities.HasShroud(playerCandidate))
            {
                return false;
            }

            if (Majik.Core.Rules.PlayerStaticAbilities.HasHexproof(playerCandidate)
                && !ReferenceEquals(playerCandidate, caster))
            {
                return false;
            }
        }

        // CR 115.5 / 702 — untargetability keywords. Gate on the EFFECTIVE
        // creature body, not the C# instance type: a creature-front transform
        // DFC flipped to its planeswalker back (CR 711) is a Creature instance
        // but is NOT effectively a creature, and computing it as a
        // CreatureCharacteristics throws (it now has a plain
        // PermanentCharacteristics). Reading keywords through the Permanent
        // characteristics is type-safe either way and still honours hexproof /
        // shroud / protection granted to a real creature OR an animated
        // manland. A flipped planeswalker back carrying its own untargetability
        // keywords is covered by the same permanent-level keyword read.
        if (candidate is Permanent creature && creature.IsEffectivelyCreature())
        {
            // Must still be on the battlefield (CR 115.5).
            if (creature.Zone != ZoneType.Battlefield) return false;

            // Shroud — no spell or ability may target this creature.
            if (HasKeyword(creature, "Shroud")) return false;

            // Hexproof — opponents' spells/abilities can't target.
            if (HasKeyword(creature, "Hexproof")
                && !ReferenceEquals(creature.Controller, caster))
                return false;

            // Hexproof from COLOR (CR 702.11e) — like hexproof, but only
            // against opponents' sources matching the named colour. Sungold
            // Sentinel's "gains hexproof from [chosen colour]" and Veil of
            // Summer's "hexproof from blue and from black" land the keyword
            // "Hexproof from {Colour}" on the creature; an opponent casting a
            // matching-colour spell/ability can't target it, while a
            // non-matching colour — and the controller's own spells — can.
            if (sourceColor != null
                && HasKeyword(creature, $"Hexproof from {sourceColor}")
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

    private static bool HasKeyword(Permanent c, string keyword)
    {
        if (c.ActiveEffects != null)
        {
            // Compute via the Permanent overload (returns PermanentCharacteristics,
            // a CreatureCharacteristics for an effective creature) — the
            // Creature overload casts to CreatureCharacteristics and would throw
            // on a flipped DFC computing as a plain PermanentCharacteristics.
            return c.ActiveEffects.Compute(c).Keywords.Contains(keyword);
        }
        return c.Abilities
            .OfType<Majik.Core.Abilities.KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
    }
}
