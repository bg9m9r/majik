using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 118.9 + CR 106.13 — Energy alternative cost probe. Surfaces
/// <see cref="EnergyAlternativeCost"/> candidates for the bot's spell-cast
/// enumeration.
///
/// An energy-paying card is identified by the lookup delegate (data-driven,
/// same shape as <see cref="PitchAltCostProbe"/>) — callers wire the named-
/// card factories' descriptors, or eventually an oracle-text parser. For
/// each hand-resident card the lookup returns an energy amount for, the
/// probe yields one <see cref="EnergyAlternativeCost"/> candidate IFF the
/// caster currently holds enough energy.
///
/// Probe-level filtering:
///   * Card must be in the caster's hand (CR 601.2).
///   * Caster must own the card.
///   * Caster's <see cref="Player.EnergyCounters"/> must be at least the
///     required amount (pre-filter mirrors the
///     <see cref="EnergyAlternativeCost.CanCastFor"/> check; saves the
///     bot from enumerating un-payable candidates).
///
/// The bot still calls <see cref="IAlternativeCost.CanCastFor(ICard, Player)"/>
/// before bidding, so this probe is the pre-filter, not the source of truth.
/// Composable with the other probes via the bot's
/// <see cref="AlternativeCostProbeRegistry"/>.
/// </summary>
public sealed class EnergyAltCostProbe : IAlternativeCostProbe
{
    private readonly Func<ICard, int?> _lookup;

    public EnergyAltCostProbe(Func<ICard, int?> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        var amount = _lookup(card);
        if (amount is not int n || n <= 0) yield break;
        if (caster.EnergyCounters < n) yield break;

        yield return new EnergyAlternativeCost(n);
    }

    /// <summary>
    /// Built-in lookup that recognizes the ship-list of named energy-alt-cost
    /// cards by name. Wired by callers that don't have a richer per-card
    /// metadata source. Wrath of the Skies = 4 energy.
    /// </summary>
    public static int? DefaultLookup(ICard card)
    {
        return card.Name switch
        {
            "Wrath of the Skies" => 4,
            _ => null,
        };
    }
}
