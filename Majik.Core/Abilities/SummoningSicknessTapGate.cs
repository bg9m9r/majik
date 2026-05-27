using Majik.Core.Cards;
using Majik.Core.Combat;

namespace Majik.Core.Abilities;

/// <summary>
/// CR 302.6 / 605.3a — the single engine gate for the tap ({T}) / untap
/// ({Q}) summoning-sickness restriction on activated abilities.
///
/// <para>CR 302.6: "A creature's activated ability with the tap symbol or the
/// untap symbol in its activation cost can't be activated unless the creature
/// has been under its controller's control continuously since their most
/// recent turn began." A creature satisfies this once it loses summoning
/// sickness (CR 302.1) OR if it has haste (CR 702.10).</para>
///
/// <para>CR 605.3a explicitly notes that mana abilities are NOT exempt — they
/// are activated abilities for summoning-sickness purposes, so the same gate
/// applies to a creature's "{T}: Add ..." mana ability.</para>
///
/// <para>The restriction is creature-only: lands (and other non-creature
/// permanents) tap for their abilities the turn they enter. This helper
/// therefore returns "allowed" for anything that is not a
/// summoning-sick, hasteless <see cref="Creature"/>.</para>
///
/// <para>Both activation paths route through here so the rule lives in one
/// place: <see cref="ManaAbility.CanActivate"/> (mana abilities) and
/// <see cref="Majik.Core.Costs.AdditionalCost.CanPay"/> for the {T} tap cost
/// (regular activated abilities — the choke point every {T} activated
/// ability's cost payment passes through). No per-card factory edits needed.</para>
/// </summary>
public static class SummoningSicknessTapGate
{
    /// <summary>
    /// CR 302.6 / 605.3a — true if <paramref name="source"/> may pay a {T}/{Q}
    /// activation cost right now. False only when the source is a creature that
    /// still has summoning sickness and lacks haste. Non-creature sources
    /// (lands, artifacts, …) are always allowed — the rule is creature-only.
    /// </summary>
    public static bool CanTapForAbility(object source)
    {
        if (source is not Creature creature)
        {
            // CR 302.6 gates creatures only.
            return true;
        }

        if (!creature.HasSummoningSickness)
        {
            return true;
        }

        // Summoning sick — only haste (CR 702.10) lifts the restriction.
        return CombatAbilities.HasHaste(creature);
    }
}
