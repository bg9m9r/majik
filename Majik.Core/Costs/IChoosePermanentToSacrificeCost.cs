using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// CR 700.6 / 701.16 — an activated-ability COST that requires the controller
/// to sacrifice a PERMANENT (of an arbitrary type/subtype filter) THEY choose,
/// e.g. "Sacrifice a Desert" (Ramunap Ruins / Scavenger Grounds) or "Sacrifice
/// a token". This is the permanent-level analogue of
/// <see cref="IChooseCreatureToSacrificeCost"/>: the creature interface only
/// covers "Sacrifice a/another creature" costs, so a typed NON-creature
/// sacrifice (a land subtype, a token, an artifact) was never offered a prompt
/// in the live dispatch path — it silently auto-picked the first eligible
/// permanent.
///
/// <para>Because <see cref="ICost.Pay"/> is synchronous and has no agent, the
/// chosen permanent must be supplied by the activation dispatch BEFORE the cost
/// is paid: <c>SacrificeCostPrompt.ChooseSacrificesAsync</c> enumerates
/// <see cref="EligiblePermanents"/>, prompts the controller to pick one (the
/// same <c>ChooseAsync</c> PickOne prompt the portal already renders as a
/// <c>ChoiceCommand</c> — no new wire contract), and assigns it via
/// <see cref="ChoosePermanent"/>. <see cref="ICost.Pay"/> then sacrifices the
/// chosen permanent.</para>
///
/// <para>Without this hook the cost auto-picked the first eligible permanent
/// with no prompt — the live-play bug this interface fixes for typed non-self
/// (non-creature) sacrifice costs.</para>
/// </summary>
public interface IChoosePermanentToSacrificeCost : ICost
{
    /// <summary>The permanents the controller may choose to sacrifice for this
    /// cost, enumerated against the player's current battlefield.</summary>
    IReadOnlyList<Permanent> EligiblePermanents(Player player);

    /// <summary>Record the controller's chosen permanent so <see cref="ICost.Pay"/>
    /// sacrifices it. Null clears the choice (falls back to legacy auto-pick).</summary>
    void ChoosePermanent(Permanent? permanent);
}
