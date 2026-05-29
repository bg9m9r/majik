using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hushwood Verge (Bloomburrow).
///
/// GW Verge cycle — counterpart to Gloomlake Verge (UB), Wastewood Verge
/// (GB), Sunsplit Verge (RW), Gleamfield Verge (GW counterpart?), and the
/// other Verges. Shares the exact "untapped color + restricted color"
/// shape of <see cref="GloomlakeVergeFactory"/>.
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {G}.
///    {T}: Add {W}. Activate only if you control a Forest or a Plains."
///
/// ## Rules notes
///
/// CR 605.1 — Both abilities are mana abilities; they do not use the stack
/// and cannot be responded to.
///
/// CR 605.4 — "Activate only if …" is an activation restriction on ability 2.
/// It is checked before paying the cost ({T}). If the controller does not
/// control a Forest or a Plains at activation time, the {W} ability is
/// illegal and may not be activated.
///
/// Tap contention (CR 605.3 / 306.5) — both abilities list {T} as their
/// activation cost. Tapping the land to produce {G} leaves the land tapped,
/// which then blocks the {W} ability (and vice versa) for the remainder of
/// the untap cycle. Standard "same tap cost" mutual exclusion; no special
/// rule needed.
///
/// "Control a Forest or a Plains" (CR 305.6 / 205.3i) — any permanent the
/// controller controls on the battlefield whose subtype list includes Forest
/// OR Plains satisfies the restriction. Basic Forest/Plains, nonbasic
/// Forest/Plains-typed dual lands (e.g. Temple Garden), and subtype-granting
/// effects all qualify.
///
/// ## JSON definition
///
/// The data-only schema (<see cref="Majik.Core.CardData.Definitions.ManaAbilityDefinition"/>)
/// carries the card's identity + the two mana colors, but it has no field
/// for the "activate only if" predicate (a closure that reads battlefield
/// state). So the restriction is wired here in code, mirroring the working
/// sibling <see cref="GloomlakeVergeFactory"/>. The JSON file
/// (<c>hushwood-verge.json</c>) documents the structural shape; this factory
/// is the authoritative builder.
/// </summary>
[CardName("Hushwood Verge")]
public static class HushwoodVergeFactory
{
    /// <summary>
    /// Construct Hushwood Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Hushwood Verge");
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {G}
        // CR 605.1 — mana ability; does not use the stack.
        // No activation restriction.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));

        // ----------------------------------------------------------------
        // {T}: Add {W}. Activate only if you control a Forest or a Plains.
        // CR 605.1 — mana ability; does not use the stack.
        // CR 605.4 / 605.3b — "Activate only if …" predicate enforced via
        // canActivateCheck.  Checks whether any permanent on the controller's
        // battlefield has the Forest or Plains subtype (CR 305.6 / 205.3i).
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
                // at least one permanent with subtype Forest or Plains.
                // CR 305.6 / 205.3i — any permanent (land, creature-land, etc.)
                // with the Forest or Plains subtype qualifies.
                return owner.Zones.Battlefield.GetCards()
                    .Any(c => c.HasSubtype(CardSubtype.Forest)
                           || c.HasSubtype(CardSubtype.Plains));
            }));

        return land;
    }
}
