using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thornspire Verge (Duskmourn: House of Horror).
///
/// RG Verge cycle — counterpart to Gloomlake Verge (UB), Wastewood Verge (GB),
/// Sunsplit Verge (RW), Gleamfield Verge (GW), and Floodfarm Verge (UR).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {R}.
///    {T}: Add {G}. Activate only if you control a Mountain or a Forest."
///
/// ## Why a hand-coded factory (not a JSON definition)
///
/// The data-driven <see cref="Majik.Core.CardData.Definitions.ManaAbilityDefinition"/>
/// schema only carries a <c>produces</c> color — it has no field for the
/// "Activate only if you control …" activation restriction (CR 605.4). A
/// JSON-only definition would silently drop the gate and produce {G}
/// unconditionally. So this card follows the proven Gloomlake Verge analogue
/// (<see cref="GloomlakeVergeFactory"/>), enforcing the restriction with a
/// <c>canActivateCheck</c> closure on the {G} mana ability.
///
/// ## Rules notes
///
/// CR 605.1 — Both abilities are mana abilities; they do not use the stack
/// and cannot be responded to.
///
/// CR 605.4 — "Activate only if …" is an activation restriction on the {G}
/// ability. It is checked before paying the cost ({T}). If the controller
/// does not control a Mountain or a Forest at activation time, the {G}
/// ability is illegal and may not be activated.
///
/// Tap contention (CR 605.3 / 306.5) — both abilities list {T} as their
/// activation cost. Tapping the land to produce {R} leaves the land tapped,
/// which then blocks the {G} ability (and vice versa) for the remainder of
/// the untap cycle. Standard "same tap cost" mutual exclusion; no special
/// rule needed.
///
/// "Control a Mountain or a Forest" (CR 305.6 / 205.3i) — any permanent the
/// controller controls on the battlefield whose subtype list includes
/// Mountain OR Forest satisfies the restriction. Basic Mountain/Forest,
/// nonbasic Mountain/Forest-typed dual lands (e.g. Stomping Ground), and
/// type-granting effects all qualify.
/// </summary>
[CardName("Thornspire Verge")]
public static class ThornspireVergeFactory
{
    /// <summary>
    /// Construct Thornspire Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Thornspire Verge");
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {R}
        // CR 605.1 — mana ability; does not use the stack.
        // No activation restriction.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("R")));

        // ----------------------------------------------------------------
        // {T}: Add {G}. Activate only if you control a Mountain or a Forest.
        // CR 605.1 — mana ability; does not use the stack.
        // CR 605.4 / 605.3b — "Activate only if …" predicate enforced via
        // canActivateCheck. Checks whether any permanent on the controller's
        // battlefield has the Mountain or Forest subtype (CR 305.6 / 205.3i).
        // Both tap abilities share {T} as their activation cost, so activating
        // either one taps the land and blocks the other for the same cycle
        // (tap-contention — no additional logic needed).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("G"),
            canActivateCheck: () =>
            {
                // Standard tap-cost gate: source must not already be tapped.
                // (When canActivateCheck is supplied it replaces the default
                // !IsTapped check — ManaAbility.CanActivate — so we must
                // include the tap check explicitly here.)
                if (land.IsTapped) return false;

                // CR 605.4 — activation restriction: controller must control
                // at least one permanent with subtype Mountain or Forest.
                // CR 305.6 / 205.3i — any permanent (land, creature-land, etc.)
                // with the Mountain or Forest subtype qualifies.
                return owner.Zones.Battlefield.GetCards()
                    .Any(c => c.HasSubtype(CardSubtype.Mountain)
                           || c.HasSubtype(CardSubtype.Forest));
            }));

        return land;
    }
}
