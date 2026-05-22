using System.Text.RegularExpressions;
using Majik.Core.Cards.Types;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 702.34 — extracts the "Flashback—[cost]" or "Flashback {cost}" line
/// from oracle text. The flashback cost may be:
///   - a mana cost: "Flashback {4}{R}" (Firebolt)
///   - a non-mana sacrifice: "Flashback—Sacrifice a Mountain." (Lava Dart)
///   - a combination: "Flashback {1}{R}, Sacrifice a Mountain." (hypothetical)
///
/// Returns <see cref="FlashbackDescriptor"/> with the parsed parts so
/// callers (e.g. <c>FlashbackAlternativeCost</c> + an
/// <c>IAdditionalCost</c> rider) can build the cost objects at cast time.
///
/// <para>
/// Returns <c>null</c> when no flashback line is present. Doesn't bind the
/// spell's effects — that's still <see cref="OracleSpellBinder"/>'s job.
/// Flashback parsing is separate because the rules subsystem already
/// knows the printed-cost path; we only need to expose the alternative.
/// </para>
/// </summary>
public static class FlashbackOracleParser
{
    // Em-dash and plain dash both accepted (Scryfall normalizes to em-dash).
    private static readonly Regex FlashbackLine = new(
        @"flashback\s*(?:[—\-–]|\s)\s*(?<cost>[^.\n\r]+?)(?:\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ManaCostPattern = new(
        @"\{[^}]+\}", RegexOptions.Compiled);

    private static readonly Regex SacrificeBasicLand = new(
        @"sacrifice\s+a\s+(plains|island|swamp|mountain|forest|wastes?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static FlashbackDescriptor? TryParse(string? oracleText)
    {
        if (string.IsNullOrWhiteSpace(oracleText)) return null;

        var match = FlashbackLine.Match(oracleText);
        if (!match.Success) return null;

        var costText = match.Groups["cost"].Value.Trim();
        if (costText.Length == 0) return null;

        // Mana portion: glue together any {...} symbols.
        var manaSymbols = ManaCostPattern.Matches(costText);
        var manaText = string.Concat(manaSymbols.Select(m => m.Value));
        var manaCost = string.IsNullOrEmpty(manaText) ? ManaCost.Zero : ManaCost.Parse(manaText);

        // Sacrifice-basic-land rider (Lava Dart-style).
        CardSubtype? sacrificeBasicLand = null;
        var sacMatch = SacrificeBasicLand.Match(costText);
        if (sacMatch.Success)
        {
            sacrificeBasicLand = ParseBasicLandSubtype(sacMatch.Groups[1].Value);
        }

        return new FlashbackDescriptor(manaCost, sacrificeBasicLand, costText);
    }

    private static CardSubtype ParseBasicLandSubtype(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "plains" => CardSubtype.Plains,
            "island" => CardSubtype.Island,
            "swamp" => CardSubtype.Swamp,
            "mountain" => CardSubtype.Mountain,
            "forest" => CardSubtype.Forest,
            // Wastes is technically a basic land but isn't tracked as a
            // subtype on the CardSubtype enum at time of writing; treat
            // singular/plural both as Forest's neighbour-of-last-resort.
            "wastes" or "waste" => CardSubtype.Forest,
            _ => throw new ArgumentException($"Unknown basic land subtype '{s}'."),
        };
}

/// <summary>
/// Parsed shape of a "Flashback—<cost>" line.
///
///   - <see cref="ManaCost"/>: the mana portion (may be
///     <see cref="Majik.Core.ValueObjects.ManaCost.Zero"/> when the
///     flashback is purely non-mana, e.g. Lava Dart).
///   - <see cref="SacrificeBasicLandSubtype"/>: when present, the caller
///     should attach a <c>SacrificeBasicLandCost</c> alongside the
///     <c>FlashbackAlternativeCost</c> via <see cref="Majik.Core.Game.SpellCastFlow"/>'s
///     <c>additionalCosts</c> parameter.
///   - <see cref="RawCostText"/>: original text after the em-dash, for
///     diagnostics / unrecognized riders.
/// </summary>
public sealed record FlashbackDescriptor(
    ManaCost ManaCost,
    CardSubtype? SacrificeBasicLandSubtype,
    string RawCostText);
