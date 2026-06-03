using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// Intensity / Intensify (Mystery Booster 2 — Static Discharge and the
/// Intensity-counter family).
///
/// <para>
/// <b>Intensity</b> is a card-scoped numeric value. A card with the Intensity
/// keyword has a printed "Starting intensity N"; the value is tracked on the
/// <see cref="Card"/> itself (<see cref="Card.Intensity"/>), NOT as a counter
/// on a permanent — the printed Intensity cards are instants/sorceries that
/// never become permanents, and the value must persist while the card sits in
/// the graveyard / hand / library between casts. X-valued effects on the card
/// read the live value ("deals damage equal to its intensity").
/// </para>
///
/// <para>
/// <b>Intensify N</b> is the action that raises a card's intensity by N. Static
/// Discharge's resolution reads "Then cards you own named Static Discharge
/// intensify by 1" — so on resolution, EVERY card the spell's owner owns with
/// that name (in any zone) is raised by 1, including the resolving spell itself
/// (which is on its way to the graveyard) and any future copies still in the
/// library / hand. The next time the owner casts a Static Discharge, it deals
/// its now-higher intensity in damage.
/// </para>
///
/// <para>
/// Wiring posture mirrors <see cref="AdaptFactory"/>: <see cref="Build"/>
/// stamps the printed starting intensity and a <see cref="KeywordAbility"/>
/// marker (so inspectors / tooltips can see "Intensity N"); the card's factory
/// supplies the damage body that reads <see cref="Card.Intensity"/> and calls
/// <see cref="IntensifyOwnedCopies"/> on resolution.
/// </para>
/// </summary>
public static class IntensifyHelper
{
    /// <summary>
    /// Stamp the printed starting intensity on <paramref name="card"/> and
    /// attach an "Intensity N" keyword marker. Called at card-build time by an
    /// Intensity card's factory.
    /// </summary>
    /// <param name="card">The card carrying the Intensity keyword. Must be
    /// non-null and have an owner.</param>
    /// <param name="startingIntensity">The printed "Starting intensity N"
    /// value. Must be &gt;= 0; printed values are positive.</param>
    public static void Build(Card card, int startingIntensity)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (startingIntensity < 0)
            throw new ArgumentOutOfRangeException(
                nameof(startingIntensity), startingIntensity,
                "Starting intensity must be non-negative.");

        var controller = card.Controller ?? card.Owner;

        card.SetStartingIntensity(startingIntensity);
        card.AddAbility(new KeywordAbility(
            $"Intensity {startingIntensity}", card, controller));
    }

    private static readonly Regex StartingIntensityPattern = new(
        @"^\s*Starting\s+intensity\s+(?<n>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Parse a "Starting intensity N" line out of <paramref name="oracleText"/>
    /// and stamp it on <paramref name="card"/> via <see cref="Build"/>. No-op
    /// when the card has no Intensity keyword (no "Starting intensity" line).
    /// Called by <see cref="Majik.Core.CardData.ScryfallCardFactory"/> so a
    /// deck-loaded Intensity card carries its printed starting value the moment
    /// it is built (the live cast then reads it through
    /// <see cref="IntensityOf"/>).
    /// </summary>
    /// <returns><c>true</c> when a starting intensity was found and stamped.</returns>
    public static bool ApplyStartingIntensityFromOracle(Card card, string? oracleText)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (string.IsNullOrEmpty(oracleText)) return false;

        var m = StartingIntensityPattern.Match(oracleText);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups["n"].Value, out var n)) return false;

        Build(card, n);
        return true;
    }

    /// <summary>
    /// "Cards you own named <paramref name="cardName"/> intensify by
    /// <paramref name="amount"/>." Raises <see cref="Card.Intensity"/> by
    /// <paramref name="amount"/> on every card <paramref name="owner"/> owns
    /// with that name, across ALL zones (battlefield, graveyard, hand, library,
    /// exile, stack). The resolving spell is itself caught (it is still on the
    /// stack at resolution time and is owned by the caster), matching the
    /// printed "Then cards you own named X intensify by 1".
    /// </summary>
    /// <param name="owner">The player whose owned cards intensify.</param>
    /// <param name="cardName">The exact card name to match (CR 201.2 — names
    /// match by the full English card name).</param>
    /// <param name="amount">N — how much each matching card intensifies by.
    /// Must be &gt; 0.</param>
    /// <returns>The number of cards that were intensified.</returns>
    public static int IntensifyOwnedCopies(Player owner, string cardName, int amount = 1)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (string.IsNullOrEmpty(cardName))
            throw new ArgumentException("Card name must be non-empty.", nameof(cardName));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Intensify amount must be positive.");

        var count = 0;
        foreach (var zoneType in AllZones)
        {
            var zone = owner.Zones.GetZone(zoneType);
            foreach (var card in zone.GetCards())
            {
                if (card is Card c
                    && c.Owner == owner
                    && string.Equals(c.Name, cardName, StringComparison.Ordinal))
                {
                    c.Intensify(amount);
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Read the current intensity of <paramref name="owner"/>'s cards named
    /// <paramref name="cardName"/>. Because every copy a player owns
    /// intensifies together (the printed "cards you own named X intensify"
    /// keeps them in lock-step), any owned copy reports the same value — so
    /// the resolving spell ("deals damage equal to its intensity") can read
    /// the value off whichever owned copy is found, including the one
    /// currently on the stack. Returns <c>0</c> when the owner has no such
    /// card in any zone (the printed starting intensity is always &gt; 0, so
    /// 0 means "none found" — a safe no-damage fallback).
    /// </summary>
    public static int IntensityOf(Player owner, string cardName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (string.IsNullOrEmpty(cardName))
            throw new ArgumentException("Card name must be non-empty.", nameof(cardName));

        foreach (var zoneType in AllZones)
        {
            var zone = owner.Zones.GetZone(zoneType);
            foreach (var card in zone.GetCards())
            {
                if (card is Card c
                    && c.Owner == owner
                    && string.Equals(c.Name, cardName, StringComparison.Ordinal))
                {
                    return c.Intensity;
                }
            }
        }

        return 0;
    }

    private static readonly ZoneType[] AllZones =
    {
        ZoneType.Battlefield,
        ZoneType.Graveyard,
        ZoneType.Hand,
        ZoneType.Library,
        ZoneType.Exile,
        ZoneType.Stack,
        ZoneType.Command,
        ZoneType.Sideboard,
    };
}
