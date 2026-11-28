using System.Text.RegularExpressions;
using Majik.Core.ValueObjects;

namespace Majik.Core.Api;

/// <summary>
/// CR 107.4 — the one wire shape for mana and costs: mana symbols in braces
/// (<c>{2}{R}</c>, <c>{G}{C}</c>, <c>{R/G}</c>, <c>{U/P}</c>). Every mana or cost
/// string field a client receives goes through here.
/// </summary>
internal static class WireMana
{
    private static readonly Regex Braced = new(
        @"^(\{[^{}]+\})+( // (\{[^{}]+\})+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A card's printed mana cost in brace form. No cost (a land,
    /// CR 202.1b) stays empty, which is not {0}; a cost already in braces,
    /// including a split card's "{1}{R} // {2}{U}", is kept as printed.</summary>
    public static string Cost(string? printed)
    {
        if (string.IsNullOrWhiteSpace(printed)) return "";
        var trimmed = printed.Trim();
        if (Braced.IsMatch(trimmed)) return trimmed;
        try
        {
            return ManaCost.Parse(trimmed).ToBraceString();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            return trimmed;
        }
    }

    /// <summary>An amount of mana (produced, paid) in brace form.</summary>
    public static string Amount(ManaCost mana) => mana.ToBraceString();
}