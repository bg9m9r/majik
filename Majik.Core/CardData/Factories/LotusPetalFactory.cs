using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lotus Petal (Tempest and many reprints, {0}).
///
/// Artifact. Oracle text:
///   "{T}, Sacrifice Lotus Petal: Add one mana of any color."
///
/// ## Implementation (v1)
/// - Card identity: Artifact with mana cost {0} (non-legendary, unlike
///   the Lotus / Mox cycle).
/// - "Add one mana of any color" is modeled as five
///   <see cref="ManaAbility"/> instances (one per WUBRG) — same shape as
///   <see cref="MoxOpalFactory"/> and <see cref="DelightedHalflingFactory"/>.
/// - Each ability uses the (source, controller, manaGenerated, canActivateCheck,
///   additionalCostPayer) constructor: <c>canActivateCheck</c> ANDs
///   <c>!IsTapped</c> with "Lotus Petal is still on the battlefield" so the
///   ability can only be activated once; <c>additionalCostPayer</c> performs
///   the sacrifice (CR 701.16) inline by moving the petal from its
///   controller's battlefield to its owner's graveyard.
/// - CR 605.1 — the ability is still a mana ability (no stack); the
///   sacrifice cost rides the activation as part of the cost, not a
///   resolution effect, matching how Horizon Canopy / painlands wire
///   non-mana additional costs through the same constructor.
/// - Sacrifice payment is a no-op stub at the engine level today (same as
///   Mishra's Bauble / Engineered Explosives); doing the zone move
///   directly inside <c>additionalCostPayer</c> keeps visible state aligned
///   with CR 701.16 without waiting on the broader sacrifice-cost
///   plumbing.
///
/// ## Deferred (v1 gaps)
/// - "Mana of any color" is bound as five separate ManaAbility instances;
///   the bot's source-picker selects the right colour at payment time.
///   A single modal-colour ManaAbility (single ability, choose colour at
///   activation) is not in the engine yet — same pattern as Mox Opal /
///   Delighted Halfling / City of Brass.
/// </summary>
[CardName("Lotus Petal")]
public static class LotusPetalFactory
{
    public const string CardName = "Lotus Petal";
    public const string Cost = "{0}";

    /// <summary>
    /// Construct Lotus Petal owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var petal = new Artifact(
            CardName,
            Cost,
            supertypes: null,
            subtypes: null);
        petal.SetOwner(owner);
        petal.SetController(owner);

        // --------------------------------------------------------------
        // {T}, Sacrifice Lotus Petal: Add one mana of any color.
        // Five ManaAbility instances, one per WUBRG. Each is gated on:
        //   (1) Lotus Petal is untapped, AND
        //   (2) Lotus Petal is still on the battlefield (i.e. not yet
        //       sacrificed by a sibling activation in the same step).
        // The additionalCostPayer performs the sacrifice (CR 701.16)
        // inline — moves the petal from its controller's battlefield to
        // its owner's graveyard. CR 605.1 keeps the activation off the
        // stack despite the non-{T} cost.
        // --------------------------------------------------------------
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            petal.AddAbility(new ManaAbility(
                source: petal,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !petal.IsTapped
                                        && petal.Zone == ZoneType.Battlefield,
                additionalCostPayer: _ => SacrificePetal(petal)));
        }

        return petal;
    }

    /// <summary>
    /// CR 701.16 — sacrifice: the owner moves their permanent from the
    /// battlefield to their graveyard. Idempotent: if Lotus Petal has
    /// already been moved (defensive — shouldn't happen given the
    /// canActivateCheck gate) we no-op.
    /// </summary>
    private static void SacrificePetal(Artifact petal)
    {
        if (petal.Zone != ZoneType.Battlefield) return;

        var controller = petal.Controller;
        var owner = petal.Owner;
        if (controller == null || owner == null) return;

        controller.Zones.Battlefield.RemoveCard(petal);
        owner.Zones.Graveyard.AddCard(petal);
        petal.SetZone(ZoneType.Graveyard);
    }
}
