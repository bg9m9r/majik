using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blazemire Verge.
///
/// BR member of the "Verge" land cycle — counterpart to Gloomlake Verge
/// (UB), Wastewood Verge (GB), Sunsplit Verge (RW), Gleamfield Verge (GW),
/// and Floodfarm Verge (UR).
///
/// Land. Oracle text (verified against Scryfall):
///   "{T}: Add {B}.
///    {T}: Add {R}. Activate only if you control a Swamp or a Mountain."
///
/// ## Card identity comes from JSON
///
/// The card's name/type and the unconditional {B} mana ability are loaded
/// from the embedded JSON definition (<c>blazemire-verge.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>, matching the data-driven authoring
/// pattern used by the rest of the cycle.
///
/// The conditional {R} ability cannot be expressed in the current
/// <see cref="ManaAbilityDefinition"/> schema (it carries no "activate only
/// if" predicate), so it is attached here in code. Mirrors the
/// restriction-enforcing approach of <c>GloomlakeVergeFactory</c> rather
/// than the restriction-deferring stub in <c>WastewoodVergeFactory</c>.
///
/// ## Rules notes
///
/// CR 605.1 — Both abilities are mana abilities; they do not use the stack
/// and cannot be responded to.
///
/// CR 605.4 — "Activate only if …" is an activation restriction on the {R}
/// ability. It is checked before paying the cost ({T}). If the controller
/// does not control a Swamp or a Mountain at activation time, the {R}
/// ability is illegal and may not be activated.
///
/// Tap contention (CR 605.3 / 306.5) — both abilities list {T} as their
/// activation cost. Tapping the land to produce {B} leaves the land tapped,
/// which then blocks the {R} ability (and vice versa) for the remainder of
/// the untap cycle. Standard "same tap cost" mutual exclusion; no special
/// rule needed.
///
/// "Control a Swamp or a Mountain" (CR 305.6 / 205.3i) — any permanent the
/// controller controls on the battlefield whose subtype list includes Swamp
/// OR Mountain satisfies the restriction. Basic Swamp/Mountain, nonbasic
/// dual lands with those types (e.g. Blood Crypt), and type-granting
/// effects (e.g. Urborg) all qualify.
/// </summary>
[CardName("Blazemire Verge")]
public static class BlazemireVergeFactory
{
    /// <summary>
    /// Construct Blazemire Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the unconditional {B} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("blazemire-verge");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add {R}. Activate only if you control a Swamp or a Mountain.
        // CR 605.1 — mana ability; does not use the stack.
        // CR 605.4 / 605.3b — "Activate only if …" predicate enforced via
        // canActivateCheck. Checks whether any permanent on the controller's
        // battlefield has the Swamp or Mountain subtype (CR 305.6 / 205.3i).
        // Both tap abilities share {T} as their activation cost, so
        // activating either one taps the land and blocks the other for the
        // same cycle (tap-contention — no additional logic needed).
        //
        // Added in code rather than JSON because the ManaAbilityDefinition
        // schema cannot express an "activate only if" predicate.
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
                // at least one permanent with subtype Swamp or Mountain.
                // CR 305.6 / 205.3i — any permanent (land, creature-land, etc.)
                // with the Swamp or Mountain subtype qualifies.
                return owner.Zones.Battlefield.GetCards()
                    .Any(c => c.HasSubtype(CardSubtype.Swamp)
                           || c.HasSubtype(CardSubtype.Mountain));
            }));

        return land;
    }
}
