using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Riverpyre Verge (Duskmourn: House of Horror).
///
/// UR Verge cycle — counterpart to Gloomlake Verge (UB), Wastewood Verge (GB),
/// Sunsplit Verge (RW), and Gleamfield Verge (GW).
///
/// Land. Oracle text:
///   "{T}: Add {R}.
///    {T}: Add {U}. Activate only if you control an Island or a Mountain."
///
/// ## Rules notes
///
/// CR 605.1 — Both abilities are mana abilities; they do not use the stack
/// and cannot be responded to.
///
/// CR 605.4 — "Activate only if …" is an activation restriction on ability 2.
/// It is checked before paying the cost ({T}). If the controller does not
/// control an Island or a Mountain at activation time, the {U} ability is
/// illegal and may not be activated.
///
/// Tap contention (CR 605.3 / 306.5) — both abilities list {T} as their
/// activation cost. Tapping the land to produce {R} leaves the land tapped,
/// which then blocks the {U} ability (and vice versa) for the remainder of
/// the untap cycle. Standard "same tap cost" mutual exclusion; no special
/// rule needed.
///
/// "Control an Island or a Mountain" (CR 305.6 / 205.3i) — any permanent the
/// controller controls on the battlefield whose subtype list includes Island
/// OR Mountain satisfies the restriction. Basic Mountain, nonbasic
/// Mountain-typed dual lands (e.g. Steam Vents), and any Island-typed
/// permanents all qualify.
/// </summary>
[CardName("Riverpyre Verge")]
public static class RiverpyreVergeFactory
{
    /// <summary>
    /// Construct Riverpyre Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Riverpyre Verge");
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {R}
        // CR 605.1 — mana ability; does not use the stack.
        // No activation restriction.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // ----------------------------------------------------------------
        // {T}: Add {U}. Activate only if you control an Island or a Mountain.
        // CR 605.1 — mana ability; does not use the stack.
        // CR 605.4 / 605.3b — "Activate only if …" predicate enforced via
        // canActivateCheck.  Checks whether any permanent on the controller's
        // battlefield has the Island or Mountain subtype (CR 305.6 / 205.3i).
        // Both tap abilities share {T} as their activation cost, so activating
        // either one taps the land and blocks the other for the same cycle
        // (tap-contention — no additional logic needed).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("U"),
            canActivateCheck: () =>
            {
                // Standard tap-cost gate: source must not already be tapped.
                // (When canActivateCheck is supplied it replaces the default
                // !IsTapped check — ManaAbility.CanActivate — so we must
                // include the tap check explicitly here.)
                if (land.IsTapped) return false;

                // CR 605.4 — activation restriction: controller must control
                // at least one permanent with subtype Island or Mountain.
                // CR 305.6 / 205.3i — any permanent (land, creature-land, etc.)
                // with the Island or Mountain subtype qualifies.
                return owner.Zones.Battlefield.GetCards()
                    .Any(c => c.HasSubtype(CardSubtype.Island)
                           || c.HasSubtype(CardSubtype.Mountain));
            }));

        return land;
    }
}
