using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// "As an additional cost to cast this spell, exile N cards from your
/// graveyard." (CR 601.2f.) Generic shape — no card-type restriction
/// (sibling of <see cref="ExileCreaturesFromGraveyardAdditionalCost"/>
/// which gates picks to <see cref="Cards.Types.CardType.Creature"/>).
///
/// First consumer: Abhorrent Oculus (Duskmourn: House of Horror) —
/// "As an additional cost to cast this spell, exile six cards from
/// your graveyard." Future consumers: Treasure Cruise / Dig Through
/// Time's Delve cousins that print as additional costs rather than
/// alternative costs (none in Modern today, but the shape is here for
/// reuse).
///
/// <see cref="Exiled"/> captures the cards exiled by <see cref="Pay"/>
/// so downstream effects that reference "the exiled cards" can read
/// them (Oculus doesn't need this hook but it parallels
/// <see cref="ExileCreaturesFromGraveyardAdditionalCost.Exiled"/>).
/// </summary>
public sealed class ExileCardsFromGraveyardAdditionalCost : IAdditionalCost
{
    private readonly int _count;
    private readonly List<ICard> _exiled = new();

    /// <summary>
    /// The cards actually exiled once <see cref="Pay"/> has succeeded.
    /// Empty before payment.
    /// </summary>
    public IReadOnlyList<ICard> Exiled => _exiled.AsReadOnly();

    /// <summary>Number of cards required to be exiled.</summary>
    public int Count => _count;

    public ExileCardsFromGraveyardAdditionalCost(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        _count = count;
    }

    /// <inheritdoc/>
    public string Description => $"exile {_count} cards from your graveyard";

    /// <inheritdoc/>
    /// <remarks>
    /// CR 601.2f — legality is checked at cast-announcement time. True
    /// when the caster's graveyard contains at least <c>_count</c>
    /// cards (any card type — distinct from the
    /// <see cref="ExileCreaturesFromGraveyardAdditionalCost"/> sibling).
    /// </remarks>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;
        return caster.Zones.Graveyard.GetCards().Count() >= _count;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// CR 601.2f payment — picks the first N cards from the caster's
    /// graveyard (deterministic v1, no agent prompt yet) and moves
    /// them Graveyard → Exile via raw zone mutation (parallels
    /// <see cref="ExileCreaturesFromGraveyardAdditionalCost.Pay"/> and
    /// <see cref="Majik.Core.CardData.Factories.ScavengingOozeFactory"/>'s
    /// exile path). Returns false (no payment) when the graveyard is
    /// too small to cover the cost.
    /// </remarks>
    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;

        var picks = caster.Zones.Graveyard.GetCards()
            .Take(_count)
            .ToList();

        foreach (var pick in picks)
        {
            caster.Zones.Graveyard.RemoveCard(pick);
            caster.Zones.Exile.AddCard(pick);
            pick.SetZone(ZoneType.Exile);
            _exiled.Add(pick);
        }

        return true;
    }
}
