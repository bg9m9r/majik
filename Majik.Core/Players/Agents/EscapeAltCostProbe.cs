using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.138 — Escape alternative-cost probe. Surfaces
/// <see cref="EscapeAlternativeCost"/> candidates for the bot's spell-cast
/// enumeration.
///
/// An Escape-bearing card is identified by the lookup delegate
/// (data-driven, same shape as <see cref="PitchAltCostProbe"/> /
/// <see cref="EnergyAltCostProbe"/>) — callers wire the named-card
/// factories' descriptors, or eventually an oracle-text parser. For
/// each graveyard-resident card the lookup returns a descriptor for,
/// the probe yields one <see cref="EscapeAlternativeCost"/> candidate
/// IFF the caster has enough OTHER cards in graveyard to satisfy the
/// exile rider.
///
/// Probe-level filtering:
///   * Card must be in the caster's graveyard (CR 702.138a — Escape
///     functions only while the card is in a graveyard).
///   * Caster must own the card ("your graveyard").
///   * Caster's graveyard must contain at least
///     <see cref="EscapeDescriptor.ExileCount"/> OTHER cards (pre-filter
///     mirrors <see cref="EscapeAlternativeCost.IsLegalInContext"/>;
///     saves the bot from enumerating unpayable candidates).
///
/// The bot still calls <see cref="IAlternativeCost.CanCastFor(ICard, Player)"/>
/// before bidding, so this probe is the pre-filter, not the source of
/// truth. Composable with the other probes via the bot's
/// <see cref="AlternativeCostProbeRegistry"/>.
/// </summary>
public sealed class EscapeAltCostProbe : IAlternativeCostProbe
{
    /// <summary>Descriptor of a printed Escape cost on a card. <see cref="ExileCount"/>
    /// is the number of OTHER graveyard cards the Escape rider exiles.</summary>
    public readonly record struct EscapeDescriptor(ManaCost EscapeManaCost, int ExileCount);

    private readonly Func<ICard, EscapeDescriptor?> _lookup;

    public EscapeAltCostProbe(Func<ICard, EscapeDescriptor?> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        if (card.Zone != ZoneType.Graveyard) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        var desc = _lookup(card);
        if (desc is not { } d) yield break;
        if (d.ExileCount <= 0) yield break;

        // Pre-filter: caster must have ExileCount OTHER cards in
        // graveyard (matches EscapeAlternativeCost.IsLegalInContext).
        var others = caster.Zones.Graveyard.GetCards()
            .Count(c => !ReferenceEquals(c, card));
        if (others < d.ExileCount) yield break;

        yield return new EscapeAlternativeCost(d.EscapeManaCost, d.ExileCount);
    }

    /// <summary>
    /// Built-in lookup that recognizes the ship-list of named Escape cards
    /// by name AND honours any runtime Escape grant stamped via
    /// <see cref="Majik.Core.Cards.Card.GrantRuntimeEscape"/> (Underworld
    /// Breach's static "Each nonland card in your graveyard has escape …"
    /// grant). The runtime stamp takes precedence over the printed ship
    /// list so a card that already has printed escape but also receives a
    /// runtime grant is surfaced under the granted cost (the printed shape
    /// remains available for direct cost construction via the card's
    /// factory <c>BuildAlternativeCost()</c>).
    ///
    /// Wired by callers that don't have a richer per-card metadata source.
    /// Per Scryfall printings:
    /// <list type="bullet">
    ///   <item>Uro, Titan of Nature's Wrath: <c>{G}{G}{U}{U}</c> + 5 exile</item>
    ///   <item>Phlage, Titan of Fire's Fury: <c>{R}{R}{W}{W}</c> + 5 exile</item>
    ///   <item>Phoenix of Ash: <c>{3}{R}{R}</c> + 4 exile</item>
    ///   <item>Cling to Dust: <c>{3}{B}</c> + 5 exile</item>
    /// </list>
    /// </summary>
    public static EscapeDescriptor? DefaultLookup(ICard card)
    {
        // Runtime grant (CR 702.143 — Underworld Breach et al.) takes
        // precedence over the printed ship list. Underworld Breach stamps
        // every nonland graveyard card with GrantRuntimeEscape(
        // card.printedCost, 3); the probe surfaces the stamped cost +
        // count directly.
        if (card is Majik.Core.Cards.Card concrete
            && concrete.RuntimeEscapeCost is { } cost
            && concrete.RuntimeEscapeExileCount is { } count)
        {
            return new EscapeDescriptor(cost, count);
        }

        return card.Name switch
        {
            "Uro, Titan of Nature's Wrath" => new EscapeDescriptor(ManaCost.Parse("{G}{G}{U}{U}"), 5),
            "Phlage, Titan of Fire's Fury" => new EscapeDescriptor(ManaCost.Parse("{R}{R}{W}{W}"), 5),
            "Kroxa, Titan of Death's Hunger" => new EscapeDescriptor(ManaCost.Parse("{B}{B}{R}{R}"), 5),
            "Phoenix of Ash"               => new EscapeDescriptor(ManaCost.Parse("{3}{R}{R}"), 4),
            "Cling to Dust"                => new EscapeDescriptor(ManaCost.Parse("{3}{B}"), 5),
            _ => null,
        };
    }
}
