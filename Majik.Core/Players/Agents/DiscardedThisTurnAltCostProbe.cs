using Majik.Core.Cards;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 118.9 — discard-gated alternative cast cost probe. Surfaces
/// <see cref="DiscardedThisTurnAlternativeCost"/> candidates for the bot's
/// spell-cast enumeration: "As long as you've discarded a card this turn,
/// you may pay [cost] to cast this spell."
/// (Asmoranomardicadaistinaculdacar, Modern Horizons 2.)
///
/// <para>This probe is the LIVE-engine seam that was previously missing: the
/// {B/R} permission existed only as a caller-built cost
/// (<see cref="AsmoranomardicadaistinaculdacarFactory.BuildAlternativeCost"/>)
/// that nothing in the dispatch path ever discovered. Registering this probe
/// in <see cref="AlternativeCostProbeRegistry.CreateDefault"/> means the
/// heuristic bot now auto-offers the cost whenever its gate is open, exactly
/// like Flashback / Pitch / Escape.</para>
///
/// A card carrying this cost is identified by the lookup delegate
/// (data-driven, same shape as <see cref="PitchAltCostProbe"/> /
/// <see cref="EnergyAltCostProbe"/>) — it returns the alternative mana-cost
/// STRING (e.g. <c>"{B/R}"</c>) for cards that have one, or
/// <see langword="null"/> otherwise.
///
/// <para>Probe-level filtering:</para>
/// <list type="bullet">
///   <item>Card must be in the caster's hand (CR 601.2 — Asmoran is cast
///   from hand).</item>
///   <item>Caster must own the card.</item>
///   <item>The live <see cref="GameContext.TurnState"/> must be threaded AND
///   the caster's discard tally for the turn must be &gt; 0 — the same
///   per-turn counter Hollow One reads
///   (<see cref="Majik.Core.Game.TurnState.DiscardsByPlayer"/>, CR 701.16).
///   When no TurnState is threaded (shape-only context) the gate is
///   closed.</item>
/// </list>
///
/// The yielded <see cref="DiscardedThisTurnAlternativeCost"/> closes over the
/// live <c>TurnState.DiscardsByPlayer</c>, so its
/// <see cref="DiscardedThisTurnAlternativeCost.CanCastFor"/> re-reads the
/// ledger at cast time. The bot still calls
/// <see cref="IAlternativeCost.CanCastFor(ICard, Player)"/> before bidding,
/// so this probe is the pre-filter, not the source of truth. Composable with
/// the other probes via the bot's <see cref="AlternativeCostProbeRegistry"/>.
/// </summary>
public sealed class DiscardedThisTurnAltCostProbe : IAlternativeCostProbe
{
    private readonly Func<ICard, string?> _lookup;

    public DiscardedThisTurnAltCostProbe(Func<ICard, string?> lookup)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        var altManaCost = _lookup(card);
        if (string.IsNullOrEmpty(altManaCost)) yield break;

        // CR 118.9 — the gate reads the caster's own discards this turn off the
        // live TurnState. No TurnState threaded ⇒ the ledger is unreadable, so
        // the gate stays closed (mirrors DiscardedThisTurnAlternativeCost's
        // null-accessor behaviour).
        var turnState = ctx.TurnState;
        if (turnState == null) yield break;
        if (turnState.DiscardsByPlayer(caster) <= 0) yield break;

        yield return new DiscardedThisTurnAlternativeCost(
            altManaCost,
            discardCountOf: turnState.DiscardsByPlayer);
    }

    /// <summary>
    /// Built-in lookup that recognizes the ship-list of discard-gated
    /// alt-cost cards by name, returning each card's alternative mana-cost
    /// string. Wired by callers that don't have a richer per-card metadata
    /// source. Asmoranomardicadaistinaculdacar = {B/R}.
    /// </summary>
    public static string? DefaultLookup(ICard card)
    {
        return card.Name switch
        {
            AsmoranomardicadaistinaculdacarFactory.CardName
                => AsmoranomardicadaistinaculdacarFactory.AlternativeManaCost,
            _ => null,
        };
    }
}
