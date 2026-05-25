using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.8 / CR 601.2f — "As an additional cost to cast this spell,
/// pay N life." (fixed amount) or "… pay X life." (variable X driven
/// by <see cref="Card.PendingCastX"/>). Spell-time sibling of
/// <see cref="PayLifeCost"/>: <see cref="IAdditionalCost"/> shape so it
/// plugs into <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// CR 601.2f additional-cost loop alongside <see cref="KickerAdditionalCost"/>,
/// <see cref="BuybackAdditionalCost"/>, sacrifice riders, etc.
///
/// <para>
/// <b>Two flavours.</b>
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Fixed.</b> <see cref="PayLifeAdditionalCost(int)"/> — the
///     printed amount is paid verbatim (e.g. Sign in Blood-style
///     "pay 2 life" riders). Constructor validates non-negative.
///   </item>
///   <item>
///     <b>Variable X.</b>
///     <see cref="PayLifeAdditionalCost(ICard, bool)"/> with
///     <c>variableX: true</c> — the amount is read from the cast
///     card's <see cref="Card.PendingCastX"/> at <see cref="Pay"/>
///     time. <see cref="Majik.Core.Game.SpellCastFlow"/> stamps
///     <see cref="Card.PendingCastX"/> from the agent's
///     <c>ChooseXAsync</c> response BEFORE the additional-cost loop
///     fires so the value is available when <see cref="Pay"/> runs.
///     Toxic Deluge ("pay X life") is the canonical user.
///   </item>
/// </list>
///
/// <para>
/// <b>Legality (CR 119.4).</b> "Players can't pay more life than they
/// have." <see cref="CanPay"/> gates on
/// <c>caster.LifeTotal &gt;= amount</c>; an under-life caster's cast is
/// rejected at the pre-check in
/// <see cref="Majik.Core.Game.SpellCastFlow"/> before any zone mutation
/// fires (CR 601.2g — illegal cast is rewound). Paying 0 life is always
/// legal (no-op).
/// </para>
///
/// <para>
/// <b>Triggers.</b> <see cref="Pay"/> routes the deduction through
/// <see cref="Player.LoseLife"/> so any life-loss replacement /
/// triggered ability (Sanguine Bond, Vito, Vizkopa Guildmage, …) fires
/// at cast time rather than queuing on an already-resolving spell —
/// closing the "fold-into-resolve" deferral that the original Toxic
/// Deluge factory carried.
/// </para>
///
/// <para>
/// <b>Why this lives separately from <see cref="PayLifeCost"/>.</b>
/// <see cref="PayLifeCost"/> implements <see cref="ICost"/> for
/// <em>activated</em>-ability cost lists (CR 118.8 — Phyrexian Tower,
/// Necropotence, …). This class implements <see cref="IAdditionalCost"/>
/// for <em>spell</em>-time additional costs (CR 601.2f — Toxic Deluge,
/// Bond of Agony, Sign in Blood, …). The two surfaces diverge on
/// signature (<c>Pay</c> returns bool vs void) and on who consumes them
/// (<c>SpellCastFlow</c> vs <c>AbilityActivator</c>); a single type
/// can't satisfy both contracts cleanly.
/// </para>
/// </summary>
public sealed class PayLifeAdditionalCost : IAdditionalCost
{
    private readonly int _fixedAmount;
    private readonly ICard? _cardForVariableX;
    private readonly bool _variableX;

    /// <summary>
    /// The life amount actually paid by the most recent <see cref="Pay"/>
    /// call. Null until <see cref="Pay"/> succeeds. Lets effect closures
    /// read the paid X off the cost reference (via
    /// <c>ChosenSpellParams.AdditionalCostPayments</c>) when the resolve
    /// body needs the same magnitude (Toxic Deluge's -X/-X sweep).
    /// </summary>
    public int? PaidAmount { get; private set; }

    /// <summary>
    /// Fixed-amount flavour. The cost will always pay
    /// <paramref name="amount"/> life regardless of any X stamp on the
    /// cast card. Use for "pay 2 life" / "pay 3 life" riders.
    /// </summary>
    /// <param name="amount">Non-negative life amount.</param>
    public PayLifeAdditionalCost(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount),
                "Pay-life additional cost amount must be non-negative (CR 107.1b / CR 118.8).");
        _fixedAmount = amount;
        _variableX = false;
        _cardForVariableX = null;
    }

    /// <summary>
    /// Variable-X flavour. The cost reads its amount from
    /// <paramref name="card"/>.<see cref="Card.PendingCastX"/> at
    /// <see cref="Pay"/> time —
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> stamps it from the
    /// agent's <c>ChooseXAsync</c> response before the additional-cost
    /// loop runs. A null / unset <see cref="Card.PendingCastX"/> is
    /// treated as 0 (no payment, always legal).
    /// </summary>
    /// <param name="card">The cast card whose <c>PendingCastX</c>
    /// supplies the amount. Must be non-null.</param>
    /// <param name="variableX">Must be true — present only to
    /// distinguish the variable-X overload from the fixed-amount
    /// constructor at the call site (preserves the call-site readability
    /// the issue spec asked for:
    /// <c>new PayLifeAdditionalCost(card, variableX: true)</c>).</param>
    public PayLifeAdditionalCost(ICard card, bool variableX)
    {
        _cardForVariableX = card ?? throw new ArgumentNullException(nameof(card));
        if (!variableX)
            throw new ArgumentException(
                "Use PayLifeAdditionalCost(int amount) for the fixed-amount flavour.",
                nameof(variableX));
        _variableX = true;
        _fixedAmount = 0;
    }

    /// <inheritdoc/>
    public string Description => _variableX ? "pay X life" : $"pay {_fixedAmount} life";

    /// <summary>
    /// The amount of life that would be paid right now. For the
    /// variable-X flavour this reads
    /// <see cref="Card.PendingCastX"/> off the captured card (null
    /// stamp → 0). For the fixed flavour this is the printed amount.
    /// Exposed for the pre-check pass + for cost-aware callers (bot
    /// EV, tests).
    /// </summary>
    public int GetCurrentAmount()
    {
        if (!_variableX) return _fixedAmount;
        return (_cardForVariableX as Card)?.PendingCastX ?? 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 119.4 — paying N life requires <c>LifeTotal &gt;= N</c>.
    /// Paying 0 is always legal (no-op). For variable-X, X must already
    /// have been stamped on the card (<see cref="Card.PendingCastX"/>)
    /// — <see cref="Majik.Core.Game.SpellCastFlow"/> guarantees this by
    /// prompting <c>ChooseXAsync</c> before the additional-cost pass.
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        var amount = GetCurrentAmount();
        return caster.LifeTotal >= amount;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Routes the deduction through <see cref="Player.LoseLife"/> so
    /// any life-loss replacement / triggered ability (Sanguine Bond,
    /// Vito, Vizkopa Guildmage) fires at cast time. Returns false (no
    /// partial payment) if the legality precondition fails — the cast
    /// pipeline catches this and throws to abort the cast (CR 601.2g).
    /// On success, <see cref="PaidAmount"/> latches the paid X for the
    /// resolve body to read (Toxic Deluge's -X/-X sweep).
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        var amount = GetCurrentAmount();
        if (caster.LifeTotal < amount) return false;
        if (amount > 0) caster.LoseLife(amount);
        PaidAmount = amount;
        return true;
    }
}
