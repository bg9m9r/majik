using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.121 — the "Escalate—Discard a card" additional cost paid once per
/// mode chosen beyond the first (Collective Brutality). A discard additional
/// cost (CR 601.2f / CR 701.16) with two refinements over the plain
/// <see cref="DiscardACardAdditionalCost"/>:
///
/// <list type="bullet">
/// <item><b>Excludes the spell being cast.</b> CR 702.121 — a spell can't be
/// discarded to pay its own escalate. CR 601.2a moves a hand cast to the stack
/// before cost payment, so the cast card is normally already off-hand; this
/// filter is retained defensively (and for the rarer non-hand escalate path) so
/// Collective Brutality can never discard itself. <see cref="_excluded"/> is
/// filtered out of the discardable pool.</item>
/// <item><b>Agent-driven pick.</b> When an <see cref="IPlayerAgent"/> is
/// supplied, the caster chooses which card to discard via
/// <see cref="IPlayerAgent.ChooseFromHandAsync"/> (intent
/// <see cref="BotIntent.DiscardCost"/>); otherwise it falls back to the first
/// eligible card (deterministic v1 picker, matching
/// <see cref="DiscardACardAdditionalCost"/>).</item>
/// </list>
///
/// One instance is built per extra mode, so choosing N modes creates (N − 1)
/// of these — each picks (and removes) one card, so the next instance sees a
/// smaller hand. CR 601.2g atomicity (the whole escalate bill is affordable
/// before any single discard) is enforced by the caller
/// (<c>SpellCastFlow.BuildAndPrecheckEscalateCosts</c> via <c>EscalateSpec.CanPayExtraModes</c>).
/// </summary>
public sealed class EscalateDiscardAdditionalCost : IAdditionalCost
{
    private readonly ICard _excluded;
    private readonly IPlayerAgent? _agent;

    /// <param name="excluded">The spell being cast — never eligible to be
    /// discarded to pay its own escalate.</param>
    /// <param name="agent">Optional agent for the discard pick. When null the
    /// first eligible card is discarded (v1 deterministic picker).</param>
    public EscalateDiscardAdditionalCost(ICard excluded, IPlayerAgent? agent = null)
    {
        _excluded = excluded ?? throw new ArgumentNullException(nameof(excluded));
        _agent = agent;
    }

    /// <summary>The card actually discarded by <see cref="Pay"/>. Null until
    /// payment succeeds.</summary>
    public ICard? Discarded { get; private set; }

    /// <inheritdoc/>
    public string Description => "discard a card (Escalate)";

    private List<ICard> Eligible(Player caster) =>
        caster.Zones.Hand.GetCards()
            .Where(c => !ReferenceEquals(c, _excluded))
            .ToList();

    /// <inheritdoc/>
    /// <remarks>CR 117.1 — payable only when the caster has at least one card
    /// in hand OTHER than the spell being cast.</remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return Eligible(caster).Count > 0;
    }

    /// <inheritdoc/>
    /// <remarks>CR 701.16 — the chosen card moves Hand → Graveyard.</remarks>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;
        var eligible = Eligible(caster);
        if (eligible.Count == 0) return false;

        ICard? pick = null;
        if (_agent != null)
        {
            pick = _agent
                .ChooseFromHandAsync(caster, eligible, BotIntent.DiscardCost)
                .GetAwaiter().GetResult();
            if (pick == null
                || !caster.Zones.Hand.ContainsCard(pick)
                || ReferenceEquals(pick, _excluded))
            {
                pick = eligible[0];
            }
        }
        else
        {
            pick = eligible[0];
        }

        // CR 701.8 — route through the central discard chokepoint so a
        // DiscardedEvent fires (wasCost: true).
        Majik.Core.Primitives.Fx.DiscardCard(caster, pick, wasCost: true);
        Discarded = pick;
        return true;
    }
}
