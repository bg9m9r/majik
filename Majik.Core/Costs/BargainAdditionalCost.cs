using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.169 — Bargain. "You may sacrifice an artifact, enchantment, or token
/// as you cast this spell." (Wilds of Eldraine.) An <em>optional</em>
/// additional cost (CR 601.2f / 702.169a): the caster decides at announcement
/// whether to layer this cost. When paid, the resolving spell's body branches
/// on the "if this spell was bargained" rider (CR 702.169b) — stamped via
/// <see cref="Card.WasBargained"/>, mirroring the Kicker sentinel.
///
/// <para>Because bargain is optional, this cost is only added to the cast when
/// the caster chooses to bargain; <see cref="Pay"/> performs the sacrifice and
/// stamps the spell. <see cref="CanPay"/> is true so long as the caster
/// controls at least one artifact, enchantment, or token.</para>
///
/// <para>v1 deterministic pick: the first eligible permanent the caster
/// controls is sacrificed (CR 701.16). Agent-driven "which permanent?" choice
/// shares the deferred sacrifice-picker prompt queue with the sibling
/// sacrifice costs.</para>
/// </summary>
public sealed class BargainAdditionalCost : IAdditionalCost
{
    private readonly ICard _card;

    public BargainAdditionalCost(ICard card)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card));
    }

    /// <summary>The permanent sacrificed by <see cref="Pay"/>. Null before payment.</summary>
    public Permanent? Sacrificed { get; private set; }

    public string Description => "sacrifice an artifact, enchantment, or token (Bargain)";

    /// <summary>
    /// CR 117.1 / 702.169a — payable if the caster controls at least one
    /// artifact, enchantment, or token. Bargain is optional, so a caster with
    /// nothing to sacrifice simply doesn't layer this cost.
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Any(IsBargainable);
    }

    /// <summary>
    /// CR 701.16 / 702.169b — sacrifice the first eligible permanent and stamp
    /// the spell so its "if this spell was bargained" branch fires. Returns
    /// false (no stamp) when nothing eligible is controlled.
    /// </summary>
    public bool Pay(Player caster)
    {
        if (caster == null) return false;

        var pick = caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .FirstOrDefault(IsBargainable);
        if (pick == null) return false;

        caster.Zones.Battlefield.RemoveCard(pick);
        // CR 111.7 — a token ceases to exist as an SBA after the sacrifice;
        // routing it to the graveyard first matches the engine's other
        // sacrifice paths (the SBA pass removes the token afterwards).
        caster.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
        Sacrificed = pick;

        if (_card is Card concrete) concrete.SetWasBargained(true);
        return true;
    }

    private static bool IsBargainable(Permanent p) =>
        p.IsToken
        || p.HasType(CardType.Artifact)
        || p.HasType(CardType.Enchantment);
}
