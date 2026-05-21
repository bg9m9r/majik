using Majik.Core.Abilities;
using Majik.Core.Cards;
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
/// - {T}: Add {B} mana ability — wired (restriction deferred; see below).
///
/// ## Deferred (v1 gaps)
/// - <b>"Activate only if you control a Swamp or a Forest"</b>: the {B} mana
///   ability is currently available without restriction. Enforcing this
///   requires checking the battlefield for permanents with the Swamp or
///   Forest subtype under the activating player's control at activation time.
///   Deferred until the cost-legality-check infrastructure supports
///   battlefield-state predicates.
/// </summary>
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
        // v1: restriction deferred — ability activates unconditionally.
        // CR 605.1: mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        return land;
    }
}
