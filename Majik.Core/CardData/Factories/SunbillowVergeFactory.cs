using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunbillow Verge (Tarkir: Dragonstorm).
///
/// RW Verge cycle — counterpart to Gloomlake Verge (UB), Wastewood Verge (GB),
/// Floodfarm Verge (UR), Bleachbone Verge (WB), etc.
///
/// Land. Oracle text:
///   "{T}: Add {W}.
///    {T}: Add {R}. Activate only if you control a Mountain or a Plains."
///
/// ## Rules notes
///
/// CR 605.1 — Both abilities are mana abilities; they do not use the stack
/// and cannot be responded to.
///
/// CR 605.4 — "Activate only if …" is an activation restriction on ability 2.
/// It is checked before paying the cost ({T}). If the controller does not
/// control a Mountain or a Plains at activation time, the {R} ability is
/// illegal and may not be activated.
///
/// Tap contention (CR 605.3 / 306.5) — both abilities list {T} as their
/// activation cost. Tapping the land to produce {W} leaves the land tapped,
/// which then blocks the {R} ability (and vice versa) for the remainder of
/// the untap cycle. Standard "same tap cost" mutual exclusion; no special
/// rule needed.
///
/// "Control a Mountain or a Plains" (CR 305.6 / 205.3i) — any permanent the
/// controller controls on the battlefield whose subtype list includes Mountain
/// OR Plains satisfies the restriction. Basic Mountain/Plains, nonbasic
/// Mountain/Plains-typed dual lands (e.g. Sacred Foundry), and granted
/// Mountains/Plains all qualify.
///
/// This factory mirrors <see cref="GloomlakeVergeFactory"/> (the complete
/// UB analogue), wiring the activation restriction via
/// <see cref="ManaAbility"/>'s <c>canActivateCheck</c> predicate — an
/// engine mechanic that already exists. (The JSON CardDefinition schema's
/// <c>ManaAbilityDefinition</c> cannot yet express an activation restriction,
/// so this card is built in-code rather than via CardDefinitionFactory, just
/// like Gloomlake Verge.)
/// </summary>
[CardName("Sunbillow Verge")]
public static class SunbillowVergeFactory
{
    /// <summary>
    /// Construct Sunbillow Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Sunbillow Verge");
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {W}
        // CR 605.1 — mana ability; does not use the stack.
        // No activation restriction.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("W")));

        // ----------------------------------------------------------------
        // {T}: Add {R}. Activate only if you control a Mountain or a Plains.
        // CR 605.1 — mana ability; does not use the stack.
        // CR 605.4 / 605.3b — "Activate only if …" predicate enforced via
        // canActivateCheck. Checks whether any permanent on the controller's
        // battlefield has the Mountain or Plains subtype (CR 305.6 / 205.3i).
        // Both tap abilities share {T} as their activation cost, so activating
        // either one taps the land and blocks the other for the same cycle
        // (tap-contention — no additional logic needed).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("R"),
            canActivateCheck: () =>
            {
                // Standard tap-cost gate: source must not already be tapped.
                // (When canActivateCheck is supplied it replaces the default
                // !IsTapped check — ManaAbility.CanActivate — so we must
                // include the tap check explicitly here.)
                if (land.IsTapped) return false;

                // CR 605.4 — activation restriction: controller must control
                // at least one permanent with subtype Mountain or Plains.
                // CR 305.6 / 205.3i — any permanent (land, creature-land, etc.)
                // with the Mountain or Plains subtype qualifies.
                return owner.Zones.Battlefield.GetCards()
                    .Any(c => c.HasSubtype(CardSubtype.Mountain)
                           || c.HasSubtype(CardSubtype.Plains));
            }));

        return land;
    }
}
