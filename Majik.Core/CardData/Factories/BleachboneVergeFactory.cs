using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bleachbone Verge (Duskmourn: House of Horror).
///
/// WB Verge cycle — counterpart to Gloomlake Verge (UB), Wastewood Verge (GB),
/// Sunsplit Verge (RW), Gleamfield Verge (GW), and Floodfarm Verge (UR).
///
/// Land. Oracle text:
///   "{T}: Add {B}.
///    {T}: Add {W}. Activate only if you control a Plains or a Swamp."
///
/// ## Rules notes
///
/// CR 605.1 — Both abilities are mana abilities; they do not use the stack
/// and cannot be responded to.
///
/// CR 605.4 — "Activate only if …" is an activation restriction on ability 2.
/// It is checked before paying the cost ({T}). If the controller does not
/// control a Plains or a Swamp at activation time, the {W} ability is illegal
/// and may not be activated.
///
/// Tap contention (CR 605.3 / 306.5) — both abilities list {T} as their
/// activation cost. Tapping the land to produce {B} leaves the land tapped,
/// which then blocks the {W} ability (and vice versa) for the remainder of
/// the untap cycle. Standard "same tap cost" mutual exclusion; no special
/// rule needed.
///
/// "Control a Plains or a Swamp" (CR 305.6 / 205.3i) — any permanent the
/// controller controls on the battlefield whose subtype list includes Plains
/// OR Swamp satisfies the restriction. Basic Plains/Swamp, nonbasic
/// Plains/Swamp-typed dual lands (e.g. Godless Shrine), and Urborg-granted
/// Swamps all qualify.
///
/// ## Implementation
///
/// Mirrors <see cref="GloomlakeVergeFactory"/> — the JSON/CardDefinitionFactory
/// path cannot express the "activate only if you control a subtype" mana-ability
/// restriction (ManaAbilityDefinition exposes only the produced mana), so the
/// land is built directly here using the ManaAbility canActivateCheck seam.
/// The companion JSON (CardData/Cards/bleachbone-verge.json) documents the
/// produced-mana shape for cycle parity.
/// </summary>
[CardName("Bleachbone Verge")]
public static class BleachboneVergeFactory
{
    /// <summary>
    /// Construct Bleachbone Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Bleachbone Verge");
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {B}
        // CR 605.1 — mana ability; does not use the stack.
        // No activation restriction.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        // ----------------------------------------------------------------
        // {T}: Add {W}. Activate only if you control a Plains or a Swamp.
        // CR 605.1 — mana ability; does not use the stack.
        // CR 605.4 / 605.3b — "Activate only if …" predicate enforced via
        // canActivateCheck.  Checks whether any permanent on the controller's
        // battlefield has the Plains or Swamp subtype (CR 305.6 / 205.3i).
        // Both tap abilities share {T} as their activation cost, so activating
        // either one taps the land and blocks the other for the same cycle
        // (tap-contention — no additional logic needed).
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("W"),
            canActivateCheck: () =>
            {
                // Standard tap-cost gate: source must not already be tapped.
                // (When canActivateCheck is supplied it replaces the default
                // !IsTapped check — ManaAbility.CanActivate — so we must
                // include the tap check explicitly here.)
                if (land.IsTapped) return false;

                // CR 605.4 — activation restriction: controller must control
                // at least one permanent with subtype Plains or Swamp.
                // CR 305.6 / 205.3i — any permanent (land, creature-land, etc.)
                // with the Plains or Swamp subtype qualifies.
                return owner.Zones.Battlefield.GetCards()
                    .Any(c => c.HasSubtype(CardSubtype.Plains)
                           || c.HasSubtype(CardSubtype.Swamp));
            }));

        return land;
    }
}
