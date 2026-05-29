using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Willowrush Verge (Tarkir: Dragonstorm).
///
/// GU Verge cycle — counterpart to Gloomlake Verge (UB), Wastewood Verge (GB),
/// Sunsplit Verge (RW), Gleamfield Verge (GW), and Floodfarm Verge (UR).
///
/// Land. Oracle text:
///   "{T}: Add {U}.
///    {T}: Add {G}. Activate only if you control a Forest or an Island."
///
/// ## Rules notes
///
/// CR 605.1 — Both abilities are mana abilities; they do not use the stack
/// and cannot be responded to.
///
/// CR 605.4 — "Activate only if …" is an activation restriction on ability 2.
/// It is checked before paying the cost ({T}). If the controller does not
/// control a Forest or an Island at activation time, the {G} ability is
/// illegal and may not be activated.
///
/// Tap contention (CR 605.3 / 306.5) — both abilities list {T} as their
/// activation cost. Tapping the land to produce {U} leaves the land tapped,
/// which then blocks the {G} ability (and vice versa) for the remainder of
/// the untap cycle. Standard "same tap cost" mutual exclusion; no special
/// rule needed.
///
/// "Control a Forest or an Island" (CR 305.6 / 205.3i) — any permanent the
/// controller controls on the battlefield whose subtype list includes Forest
/// OR Island satisfies the restriction. Basic Forest/Island, nonbasic
/// Forest/Island-typed dual lands, and subtype-granting effects all qualify.
///
/// The JSON definition (<c>willowrush-verge.json</c>) carries the card's
/// identity and the two unconditional mana shapes the embedded schema can
/// express; the {G} ability's "Activate only if …" predicate is not
/// representable in <see cref="Majik.Core.CardData.Definitions.ManaAbilityDefinition"/>,
/// so this factory wires both mana abilities directly to enforce CR 605.4
/// faithfully (mirrors <see cref="GloomlakeVergeFactory"/>).
/// </summary>
[CardName("Willowrush Verge")]
public static class WillowrushVergeFactory
{
    /// <summary>
    /// Construct Willowrush Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Willowrush Verge");
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {U}
        // CR 605.1 — mana ability; does not use the stack.
        // No activation restriction.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("U")));

        // ----------------------------------------------------------------
        // {T}: Add {G}. Activate only if you control a Forest or an Island.
        // CR 605.1 — mana ability; does not use the stack.
        // CR 605.4 / 605.3b — "Activate only if …" predicate enforced via
        // canActivateCheck.  Checks whether any permanent on the controller's
        // battlefield has the Forest or Island subtype (CR 305.6 / 205.3i).
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
                // at least one permanent with subtype Forest or Island.
                // CR 305.6 / 205.3i — any permanent (land, creature-land, etc.)
                // with the Forest or Island subtype qualifies.
                return owner.Zones.Battlefield.GetCards()
                    .Any(c => c.HasSubtype(CardSubtype.Forest)
                           || c.HasSubtype(CardSubtype.Island));
            }));

        return land;
    }
}
