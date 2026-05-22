using System.Text.RegularExpressions;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 702.118 — Spectacle. Recognises the oracle line
/// "Spectacle {cost} (You may cast this spell for its spectacle cost
/// rather than its mana cost if an opponent lost life this turn.)" and
/// returns a <see cref="SpectacleAlternativeCost"/> bound to the caster's
/// opponents.
///
/// Unlike <see cref="OracleSpellBinder"/> (which binds the spell's effects),
/// this binder produces an <see cref="IAlternativeCost"/> that the cast
/// dispatcher then offers to <see cref="Game.SpellCastFlow"/>. Returns
/// <c>null</c> when the oracle text has no spectacle clause OR when the
/// alt-cost is currently illegal (no opponent has lost life this turn);
/// callers fall back to the printed mana cost.
/// </summary>
public static class SpectacleBinder
{
    // "Spectacle {R}" — captures the cost expression inside the braces.
    // Tolerant of multi-symbol costs ({1}{R}, {W/B}, etc.) by allowing
    // any non-newline character before the closing paren.
    private static readonly Regex _spectacleLine = new(
        @"Spectacle\s+(?<cost>(?:\{[^}]+\})+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Try to extract the spectacle cost from <paramref name="oracleText"/>.
    /// </summary>
    /// <param name="oracleText">Full oracle text of the card.</param>
    /// <param name="cost">On success, the parsed spectacle mana cost.</param>
    /// <returns><c>true</c> if a spectacle clause was found.</returns>
    public static bool TryParseCost(string? oracleText, out ManaCost cost)
    {
        cost = ManaCost.Zero;
        if (string.IsNullOrWhiteSpace(oracleText)) return false;
        var m = _spectacleLine.Match(oracleText);
        if (!m.Success) return false;
        cost = ManaCost.Parse(m.Groups["cost"].Value);
        return true;
    }

    /// <summary>
    /// Build the alt-cost for the given caster + opponents if the oracle
    /// text contains a spectacle clause AND the caster is currently eligible
    /// to use it (some opponent has lost life this turn). Returns <c>null</c>
    /// otherwise — the caller should cast for the printed mana cost.
    /// </summary>
    /// <remarks>
    /// Eligibility check here is for convenience (so an agent doesn't have
    /// to ask); <see cref="SpectacleAlternativeCost.CanCastFor"/> is still
    /// the authoritative gate at <see cref="Game.SpellCastFlow"/>'s
    /// pre-pay step.
    /// </remarks>
    public static SpectacleAlternativeCost? TryBind(
        string? oracleText,
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (allPlayers == null) throw new ArgumentNullException(nameof(allPlayers));
        if (!TryParseCost(oracleText, out var cost)) return null;

        var opponents = new List<Player>(allPlayers.Count);
        foreach (var p in allPlayers)
        {
            if (!ReferenceEquals(p, caster)) opponents.Add(p);
        }

        // CR 702.118a — gate at bind time too so we don't hand the caller
        // an alt-cost they can't legally use. (Belt + braces with
        // SpectacleAlternativeCost.CanCastFor.)
        var anyLost = false;
        foreach (var opp in opponents)
        {
            if (opp.LifeLostThisTurn > 0) { anyLost = true; break; }
        }
        if (!anyLost) return null;

        return new SpectacleAlternativeCost(cost, opponents);
    }
}
