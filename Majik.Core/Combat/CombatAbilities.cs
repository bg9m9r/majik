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
    /// CR 509.1a — true when <paramref name="c"/> has an intrinsic "can't block"
    /// restriction (recorded as a <c>"CantBlock"</c> <see cref="KeywordAbility"/>
    /// marker on the creature itself, e.g. Mirrex's Phyrexian Mite token's quoted
    /// "This token can't block."). Distinct from the per-turn effect-installed
    /// <see cref="CombatRestriction.CannotBlock"/> (Falter / Magmatic Chasm) which
    /// CombatValidator checks separately — this is the printed/granted static.
    /// A creature with this restriction can't be declared as a blocker.
    /// </summary>
    public static bool HasCantBlock(Creature c) => Has(c, "CantBlock");

    /// <summary>
    /// CR 702.90a — Wither. A source with wither deals damage to creatures
    /// in the form of -1/-1 counters (CR 702.90b). Read at every
    /// creature-damage application site so the -1/-1-counter form is applied
    /// consistently across combat and noncombat (fight / ability) damage.
    /// </summary>
    public static bool HasWither(Creature c) => Has(c, "Wither");

    /// <summary>
    /// CR 702.90c — Infect. A source with infect deals damage to creatures
    /// in the form of -1/-1 counters (identical creature-damage form as
    /// wither) and to players in the form of poison counters. Only the
    /// creature-damage form is consumed by
    /// <see cref="DealsCreatureDamageAsMinusCounters"/>; the player → poison
    /// form is handled separately (see <see cref="InfectDamageReplacement"/>).
    /// </summary>
    public static bool HasInfect(Creature c) => Has(c, "Infect");

    /// <summary>
    /// CR 702.90b / 702.90c — true when <paramref name="source"/> deals
    /// damage to CREATURES as -1/-1 counters instead of marked damage, i.e.
    /// it has wither or infect. Centralized so combat and noncombat
    /// (fight / ability) creature-damage paths agree on the counter form.
    /// </summary>
    public static bool DealsCreatureDamageAsMinusCounters(Creature? source) =>
        source != null && (HasWither(source) || HasInfect(source));

    /// <summary>
    /// CR 702.90c — true when <paramref name="source"/> deals damage to
    /// PLAYERS as poison counters instead of life loss, i.e. it has infect
    /// (wither does NOT — wither only changes the form of damage to
    /// creatures). Centralized so combat and noncombat player-damage paths
    /// agree on the poison-counter form.
    /// </summary>
    public static bool DealsPlayerDamageAsPoison(Creature? source) =>
        source != null && HasInfect(source);

    /// <summary>
    /// CR 702.180a/b — Toxic N. Returns the total toxic value of
    /// <paramref name="source"/> (the sum of every <c>"toxic"</c>
    /// <see cref="KeywordAbility"/> marker's <see cref="KeywordAbility.Arg"/>),
    /// or 0 if the source has no toxic. CR 702.180c — multiple instances of
    /// toxic on the same creature are cumulative, so the values are summed.
    /// Unlike infect, toxic does NOT change the FORM of combat damage: a
    /// creature with toxic N that deals combat damage to a player causes that
    /// player to ALSO get N poison counters (CR 702.180b), in addition to the
    /// normal life loss. Read at the combat-damage-to-player site so the Mite
    /// (Mirrex), Pile of Rags, and the whole ONE toxic family give poison.
    /// </summary>
    public static int GetToxic(Creature? source)
    {
        if (source == null) return 0;
        var total = 0;
        foreach (var k in source.Abilities.OfType<KeywordAbility>())
        {
            if (string.Equals(k.Keyword, "toxic", StringComparison.OrdinalIgnoreCase)
                && k.Arg is int n && n > 0)
            {
                total += n;
            }
        }
        return total;
    }

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

    /// <summary>
    /// CR 509.1c / 509.1g — the "all creatures able to block this creature
    /// do so" requirement (Lure / Breaker of Armies / Nemesis Mask family).
    /// Returns true iff this attacker carries the
    /// <c>"MustBeBlockedByAllAble"</c> marker — either as a printed
    /// <see cref="KeywordAbility"/> (Breaker of Armies, inherent static) or
    /// in the layer-computed keyword set (an Aura/Equipment grant). Consulted
    /// at declare-blockers to force every creature able to block this
    /// attacker (and not otherwise required elsewhere) to do so.
    /// </summary>
    public static bool MustBeBlockedByAllAble(Creature? c) =>
        c != null && Has(c, "MustBeBlockedByAllAble");

    private static bool Has(Creature? creature, string keyword)
    {
        if (creature == null) return false;

        // Layer system source-of-truth, when wired (CR 613). Compute via the
        // PERMANENT overload (not Compute(Creature)) — a creature-front DFC
        // flipped to a NON-creature back (CR 711, e.g. a planeswalker back)
        // computes a plain PermanentCharacteristics, and Compute(Creature)
        // would throw casting it to CreatureCharacteristics. The keyword set
        // lives on the base PermanentCharacteristics, so this reads correctly
        // for both shapes.
        if (creature.ActiveEffects != null)
        {
            return creature.ActiveEffects.Compute((Permanent)creature).Keywords
                .Contains(keyword);
        }

        // Fallback: printed KeywordAbility markers.
        return creature.Abilities
            .OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
    }
}
