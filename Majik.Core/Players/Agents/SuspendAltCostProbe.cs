using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.62 — Suspend alternative-cost probe. Surfaces
/// <see cref="SuspendAlternativeCost"/> candidates for the bot's spell-cast
/// enumeration. Mirrors <see cref="EscapeAltCostProbe"/>'s data-driven
/// shape (lookup delegate that recognises printed Suspend cards by name).
///
/// <para>Probe-level filtering:</para>
/// <list type="bullet">
///   <item>Card must be in the caster's hand (CR 702.62b — Suspend is
///   paid from the hand).</item>
///   <item>Caster must own the card (alt-cost contract — only the card's
///   owner may suspend it).</item>
/// </list>
///
/// <para>The bot still calls <see cref="IAlternativeCost.CanCastFor(ICard, Player)"/>
/// before bidding, so this probe is the pre-filter, not the source of
/// truth. Composable with the other probes via the bot's
/// <see cref="AlternativeCostProbeRegistry"/>.</para>
/// </summary>
public sealed class SuspendAltCostProbe : IAlternativeCostProbe
{
    /// <summary>Descriptor of a printed Suspend cost on a card.
    /// <see cref="TimeCounters"/> is the N in "Suspend N—[cost]";
    /// <see cref="SuspendManaCost"/> is the alternative cost paid
    /// from the hand to put the card into exile with N time counters
    /// (CR 702.62a).</summary>
    public readonly record struct SuspendDescriptor(ManaCost SuspendManaCost, int TimeCounters);

    private readonly Func<ICard, SuspendDescriptor?> _lookup;

    public SuspendAltCostProbe(Func<ICard, SuspendDescriptor?> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        var desc = _lookup(card);
        if (desc is not { } d) yield break;
        if (d.TimeCounters < 0) yield break;

        yield return new SuspendAlternativeCost(d.TimeCounters, d.SuspendManaCost);
    }

    /// <summary>
    /// Built-in lookup recognising the ship-list of named Suspend cards
    /// by name. Wired by callers that don't have a richer per-card
    /// metadata source. Mirrors <see cref="EscapeAltCostProbe.DefaultLookup"/>'s
    /// shape — when oracle-text parsing for "Suspend N—[cost]" arrives the
    /// lookup will swap to a runtime parser without changing the probe
    /// surface. Per Scryfall printings:
    /// <list type="bullet">
    ///   <item>Rift Bolt: Suspend 1—{R}</item>
    ///   <item>Search for Tomorrow: Suspend 2—{G}</item>
    /// </list>
    /// </summary>
    public static SuspendDescriptor? DefaultLookup(ICard card)
    {
        return card.Name switch
        {
            "Rift Bolt"           => new SuspendDescriptor(ManaCost.Parse("{R}"), 1),
            "Search for Tomorrow" => new SuspendDescriptor(ManaCost.Parse("{G}"), 2),
            _ => null,
        };
    }
}
