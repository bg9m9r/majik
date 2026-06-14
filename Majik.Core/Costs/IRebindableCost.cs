namespace Majik.Core.Costs;

/// <summary>
/// STAGE 1 (re-sourceable abilities) — opt-in seam for an <see cref="ICost"/>
/// that captures a specific source permanent and therefore must be re-homed
/// when its owning <see cref="Majik.Core.Abilities.ActivatedAbility"/> is
/// re-sourced onto a new permanent (CR 707.2 copy machinery / Agatha's Soul
/// Cauldron granted abilities — CR 613.1f / 702.49).
///
/// <para>
/// <see cref="Majik.Core.Abilities.ActivatedAbility.RebindTo"/> already re-homes
/// <see cref="AdditionalCost"/> ({T} / sacrifice) via its bespoke
/// <see cref="AdditionalCost.RebindSource"/>. The COUNTER-payment costs
/// (<see cref="AddCounterCost"/>, <see cref="RemovePlusOnePlusOneCounterCost"/>,
/// <see cref="RemoveChargeCounterCost"/>) are bare <see cref="ICost"/>s that also
/// capture the original source — without this seam a re-homed counter-paying
/// ability would still add / remove the counter on the ORIGINAL permanent rather
/// than the bearer. Implementing this interface lets <c>RebindTo</c> swap their
/// captured source the same way it does for tap / sacrifice costs.
/// </para>
/// </summary>
public interface IRebindableCost
{
    /// <summary>
    /// Return an equivalent cost whose captured source permanent is
    /// <paramref name="newSource"/> when (and only when) the current captured
    /// source is reference-equal to <paramref name="oldSource"/>; otherwise
    /// return this instance unchanged. Implementations must be pure (no
    /// mutation of the original) so the source ability is unaffected.
    /// </summary>
    ICost RebindTo(object oldSource, object newSource);
}
