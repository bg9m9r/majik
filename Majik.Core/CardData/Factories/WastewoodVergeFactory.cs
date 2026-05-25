using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wastewood Verge (Bloomburrow).
///
/// Land. Oracle text:
///   "{T}: Add {G}.
///    {T}: Add {B}. Activate only if you control a Swamp or a Forest."
///
/// ## Implemented (v1)
/// - {T}: Add {G} mana ability — wired.
/// - {T}: Add {B} mana ability — wired with the
///   <c>canActivateCheck</c> predicate enforcing the
///   "Activate only if you control a Swamp or a Forest" restriction
///   (CR 605.1a — mana abilities do not use the stack but still
///   honour activation restrictions). The predicate samples the
///   controller's battlefield for any OTHER permanent (CR 109.2) with
///   the <see cref="CardSubtype.Swamp"/> or <see cref="CardSubtype.Forest"/>
///   subtype, so this Wastewood Verge alone cannot satisfy its own
///   restriction. Tap-as-cost legality is folded into the same
///   predicate (matches the pain-land cycle posture — once
///   <c>canActivateCheck</c> is supplied, the default
///   <see cref="ManaAbility.CanActivate"/> tap check is bypassed,
///   so the caller owns the full predicate).
/// </summary>
[CardName("Wastewood Verge")]
public static class WastewoodVergeFactory
{
    /// <summary>
    /// Construct Wastewood Verge owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Wastewood Verge");
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {G}
        // CR 605.1: mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("G")));

        // ----------------------------------------------------------------
        // {T}: Add {B}
        // Oracle: "Activate only if you control a Swamp or a Forest."
        // CR 605.1: mana abilities do not use the stack.
        // CR 109.2: "other" excludes the source itself — Wastewood Verge
        //   cannot satisfy its own restriction.
        // Pattern parallels PainLandCycleFactory / MysticSanctuaryFactory:
        // when canActivateCheck is supplied, the default !IsTapped guard
        // is bypassed, so we fold it back in here.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerated: ManaCost.Parse("B"),
            canActivateCheck: () => !land.IsTapped
                && owner.Zones.Battlefield.GetCards().Any(c =>
                    !ReferenceEquals(c, land)
                    && (c.HasSubtype(CardSubtype.Swamp)
                        || c.HasSubtype(CardSubtype.Forest)))));

        return land;
    }
}
