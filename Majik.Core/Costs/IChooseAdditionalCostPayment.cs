using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.Costs;

/// <summary>
/// CR 601.2f / CR 601.2h — a NON-MANA additional cost on a SPELL whose payment
/// requires the caster to CHOOSE which object(s) it consumes: a typed sacrifice
/// ("sacrifice an artifact or creature" — Deadly Dispute) or a variable discard
/// ("discard X cards" — Nahiri's Wrath). Because <see cref="IAdditionalCost.Pay"/>
/// is synchronous and carries no agent, the choice can't be made during payment;
/// it is collected by <see cref="Majik.Core.Game.AdditionalCostChooserPrompt"/>
/// from the cast pipeline AT the CR 601.2h payment point — AFTER target choice
/// (CR 601.2c), not at the early CanPay pre-check (CR 601.2f) — and stamped onto
/// the cost via <see cref="ApplyChoice"/> so <see cref="IAdditionalCost.Pay"/>
/// consumes exactly the chosen object(s).
///
/// <para>This is the SPELL-side analogue of the activated-ability
/// <see cref="IChoosePermanentToSacrificeCost"/> / <see cref="IChooseCreatureToSacrificeCost"/>
/// pair (prompted by <see cref="Majik.Core.Game.SacrificeCostPrompt"/>): those
/// hang off <see cref="ICost"/> on an <c>IActivatedAbility</c>, while this one
/// hangs off <see cref="IAdditionalCost"/> on a <see cref="Spells.Spell"/> cast.
/// Without this hook these costs silently auto-picked (first-eligible permanent /
/// the whole hand) with no prompt — the live-play bug the audit residual fixes
/// for the chooser-bearing spell additional costs.</para>
/// </summary>
public interface IChooseAdditionalCostPayment : IAdditionalCost
{
    /// <summary>
    /// Build the agent prompt for this cost's payment against the caster's
    /// current zones, or return <c>null</c> when no prompt is warranted (no
    /// eligible objects, or only one forced choice — the cost picks it itself).
    /// The returned request's <see cref="ChoiceRequest.Candidates"/> are the raw
    /// engine objects the agent picks among (permanents to sacrifice / cards to
    /// discard); the picks flow straight back into <see cref="ApplyChoice"/>.
    /// </summary>
    ChoiceRequest? BuildChoiceRequest(Player caster);

    /// <summary>
    /// Record the agent's chosen object(s) so <see cref="IAdditionalCost.Pay"/>
    /// consumes exactly them. An empty / null selection leaves the cost's legacy
    /// default in place (the affordability gate already accepted the cast).
    /// </summary>
    void ApplyChoice(IReadOnlyList<object> chosen);
}
