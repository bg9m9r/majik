using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// CR 700.6 / 701.17 — an activated-ability COST that requires the controller
/// to sacrifice a creature THEY choose (e.g. "Sacrifice another creature" on
/// Yawgmoth, Thran Physician / Goblin Bombardment). Because <see cref="ICost.Pay"/>
/// is synchronous and has no agent, the chosen creature must be supplied by the
/// activation dispatch BEFORE the cost is paid: the dispatch path enumerates
/// <see cref="EligibleSacrifices"/>, prompts the controller to pick one (the
/// same <c>ChooseAsync</c> prompt the portal already renders), and assigns it
/// via <see cref="ChooseSacrifice"/>. <see cref="ICost.Pay"/> then sacrifices
/// the chosen creature.
///
/// <para>Without this hook the cost auto-picked the first eligible creature
/// with no prompt — the live-play bug this interface fixes.</para>
///
/// <para>The return type is <see cref="Permanent"/> (not <see cref="Creature"/>)
/// so that non-Creature permanents that are effectively creatures via a
/// continuous Layer-4 type grant (e.g. lands animated by Badgermole Cub's
/// earthbend ability) are also included. CR 613.1c / 701.16.</para>
/// </summary>
public interface IChooseCreatureToSacrificeCost : ICost
{
    /// <summary>The permanents the controller may choose to sacrifice for this
    /// cost, enumerated against the player's current battlefield. Includes any
    /// permanent that is currently a creature via
    /// <see cref="Permanent.IsEffectivelyCreature"/> (e.g. animated lands).</summary>
    IReadOnlyList<Permanent> EligibleSacrifices(Player player);

    /// <summary>Record the controller's chosen permanent so <see cref="ICost.Pay"/>
    /// sacrifices it. Null clears the choice (falls back to legacy auto-pick).
    /// Accepts any <see cref="Permanent"/> that is currently a creature.</summary>
    void ChooseSacrifice(Permanent? permanent);
}
