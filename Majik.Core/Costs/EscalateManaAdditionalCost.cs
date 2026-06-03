using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.121 — the mana-flavoured "Escalate {cost}" additional cost paid
/// once per mode chosen beyond the first (Collective Defiance — "Escalate
/// {1}"). A pure mana additional cost (CR 601.2f): each extra mode the caster
/// chooses costs the escalate mana on top of the spell's printed cost.
///
/// <para>
/// Mirrors <see cref="KickerAdditionalCost"/>'s mana-payment shape (pay from
/// the caster's mana pool, no sentinel stamp needed) but is built once per
/// extra mode by <see cref="Majik.Core.Game.EscalateSpec.BuildPerModeCost"/>,
/// so choosing N modes creates (N − 1) of these. CR 601.2g atomicity (the
/// whole escalate bill is affordable before any single payment) is enforced
/// by the caller (<c>SpellCastFlow.PayEscalateCosts</c> via
/// <c>EscalateSpec.CanPayExtraModes</c>); each instance still re-checks its
/// own affordability so a mid-sequence shortfall can't half-pay.
/// </para>
/// </summary>
public sealed class EscalateManaAdditionalCost : IAdditionalCost
{
    private readonly ManaCost _escalateCost;

    /// <param name="escalateCost">The per-extra-mode escalate mana
    /// (e.g. <c>{1}</c> for Collective Defiance).</param>
    public EscalateManaAdditionalCost(ManaCost escalateCost)
    {
        _escalateCost = escalateCost ?? throw new ArgumentNullException(nameof(escalateCost));
    }

    public ManaCost EscalateCost => _escalateCost;

    /// <inheritdoc/>
    public string Description => $"Escalate {_escalateCost}";

    /// <inheritdoc/>
    /// <remarks>CR 601.2g — payable only when the caster's mana pool can
    /// cover the escalate cost. Probes without committing (the real spend
    /// happens in <see cref="Pay"/>).</remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.ManaPool.CanPay(_escalateCost);
    }

    /// <inheritdoc/>
    /// <remarks>CR 601.2f / CR 702.121 — pay the escalate mana from the
    /// caster's pool. Returns false (no partial spend) when the pool can't
    /// cover it.</remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;
        return caster.PayMana(_escalateCost);
    }
}
