using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Spymaster's Vault (Bloomburrow).
///
/// Land. Oracle text:
///   "This land enters tapped unless you control a Swamp.
///    {T}: Add {B}.
///    {B}, {T}: Target creature you control connives X, where X is the number
///    of creatures that died this turn. (Connive: Draw X cards, then discard
///    X cards. Put a +1/+1 counter on that creature for each nonland card
///    discarded this way.)"
///
/// ## Implemented (v1)
/// - {T}: Add {B} mana ability — wired.
///
/// ## Deferred (v1 gaps)
/// - <b>ETB-tapped restriction</b>: "enters tapped unless you control a Swamp"
///   requires a replacement-effect check on enter-the-battlefield that
///   inspects permanents of subtype Swamp under the active player's control.
///   Deferred until ETB replacement-effect infrastructure is ready.
/// - <b>Connive activated ability</b>: "{B}, {T}: Target creature you control
///   connives X" requires (a) targeting a creature you control, (b) tracking
///   the count of creatures that died this turn, (c) drawing X cards, (d)
///   discarding X cards with player choice, and (e) placing +1/+1 counters
///   for each nonland card discarded (CR 701.41: connive). All of these
///   depend on subsystems not yet available: per-turn death-count tracking,
///   targeted activation, card-draw + forced discard with player selection,
///   and counter placement on a targeted permanent.
/// </summary>
public static class SpymastersVaultFactory
{
    /// <summary>
    /// Construct Spymaster's Vault owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land("Spymaster's Vault");
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {T}: Add {B}
        // CR 605.1: mana abilities do not use the stack.
        // ----------------------------------------------------------------
        land.AddAbility(new ManaAbility(land, owner, ManaCost.Parse("B")));

        return land;
    }
}
