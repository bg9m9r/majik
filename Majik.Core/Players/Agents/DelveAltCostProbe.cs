using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.66 — Delve. Surfaces <see cref="DelveAlternativeCost"/> candidates
/// for the bot's spell-cast enumeration. A delve spell is identified by
/// the presence of a <see cref="KeywordAbility"/> labeled "Delve" on the
/// card (the ship-list of factories — Treasure Cruise, Dig Through Time,
/// Murktide Regent, Tasigur the Golden Fang, Gurmag Angler, Murderous Cut —
/// all attach this marker; see <see cref="Majik.Core.CardData.Factories"/>).
///
/// <para>Probe-level filtering:
///   * Only emits from-hand candidates; the spell must be the caster's.
///   * Skips cards with no generic pips in their printed cost (nothing to
///     reduce — Delve only swaps generic mana per CR 702.66).
///   * Skips when the caster's graveyard is empty.</para>
///
/// <para>Selection policy: by default the probe emits a SINGLE candidate
/// that exiles as many graveyard cards as the spell has generic pips (the
/// "maximum delve" pick — what a heuristic bot wants in practice: pay zero
/// generic, only the colored pips). Callers can swap a richer
/// <see cref="ChoiceStrategy"/> if they want partial-delve options (e.g.
/// preserving graveyard payoffs for Murktide / Tarmogoyf).</para>
///
/// <para>The selection is purely advisory — the actual cast-time delve
/// payment goes through
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s alt-cost arm using the
/// chosen <see cref="DelveAlternativeCost"/> instance, whose
/// <see cref="DelveAlternativeCost.OnResolved"/> exiles the cards. The
/// canonical cost-flow primitive (<see cref="DelveCost"/>) is unchanged.</para>
/// </summary>
public sealed class DelveAltCostProbe : IAlternativeCostProbe
{
    /// <summary>
    /// Strategy for picking which graveyard cards to exile. Receives the
    /// caster's graveyard and the maximum count (= the spell's generic
    /// pips); returns the selected cards. Default: take the FIRST
    /// <c>maxCount</c> cards (deterministic, matches a "max delve" policy).
    /// </summary>
    public delegate IReadOnlyList<ICard> ChoiceStrategy(
        IReadOnlyList<ICard> graveyard, int maxCount);

    private readonly Func<ICard, bool> _isDelveCard;
    private readonly ChoiceStrategy _chooser;

    public DelveAltCostProbe(
        Func<ICard, bool>? isDelveCard = null,
        ChoiceStrategy? chooser = null)
    {
        _isDelveCard = isDelveCard ?? DefaultIsDelveCard;
        _chooser = chooser ?? DefaultChooser;
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        // From-hand owner gate — delve is paid on cast (CR 702.66b), so the
        // spell needs to be in the caster's hand when we surface the option.
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        if (!_isDelveCard(card)) yield break;

        var printed = ManaCost.Parse(card.ManaCost ?? string.Empty);
        // CR 702.66 — delve reduces ONLY generic pips. If the spell has none,
        // nothing to do.
        if (printed.Generic <= 0) yield break;

        // Graveyard pool excludes the spell itself (defensive — the spell is
        // in hand, but other code paths could route it through later).
        var yard = caster.Zones.Graveyard.GetCards()
            .Where(c => !ReferenceEquals(c, card))
            .ToList();
        if (yard.Count == 0) yield break;

        var max = Math.Min(printed.Generic, yard.Count);
        var chosen = _chooser(yard, max);
        if (chosen == null || chosen.Count == 0) yield break;

        yield return new DelveAlternativeCost(printed, chosen);
    }

    /// <summary>
    /// Built-in detector: scans the card's static abilities for a
    /// <see cref="KeywordAbility"/> whose <see cref="KeywordAbility.Keyword"/>
    /// is "Delve" (case-insensitive). All shipped delve factories attach
    /// this marker.
    /// </summary>
    public static bool DefaultIsDelveCard(ICard card)
    {
        if (card == null) return false;
        return card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Delve", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Default selection: take the first <paramref name="maxCount"/>
    /// graveyard cards (insertion order). Deterministic; the bot doesn't yet
    /// preserve specific graveyard payoffs (Murktide power-counter, etc.).
    /// Callers wiring richer policies should supply a custom chooser.</summary>
    public static IReadOnlyList<ICard> DefaultChooser(
        IReadOnlyList<ICard> graveyard, int maxCount)
    {
        if (maxCount <= 0 || graveyard.Count == 0)
        {
            return Array.Empty<ICard>();
        }
        var take = Math.Min(maxCount, graveyard.Count);
        return graveyard.Take(take).ToList();
    }
}
