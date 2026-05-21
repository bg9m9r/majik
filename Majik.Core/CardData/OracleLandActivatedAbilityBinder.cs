using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Database;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// Binds activated abilities on Land cards synthesised from oracle text.
///
/// Currently covers the fetch-land cycle (Misty Rainforest, Verdant Catacombs,
/// Windswept Heath, etc.) whose oracle text follows the pattern:
///
///   "{T}, Pay 1 life, Sacrifice &lt;name&gt;: Search your library for a
///    &lt;BasicA&gt; or &lt;BasicB&gt; card, put it onto the battlefield, then shuffle."
///
/// The resulting <see cref="ActivatedAbility"/> carries three costs:
///   1. Tap the fetch land (<see cref="AdditionalCost.Tap"/>)
///   2. Pay 1 life (<see cref="AdditionalCost.PayLife"/>)
///   3. Sacrifice the fetch land (<see cref="AdditionalCost.Sacrifice"/>)
///
/// The effect searches the controller's library for the first card whose
/// <see cref="ICard.Subtypes"/> contains either target land subtype and
/// moves it directly to the battlefield. Shuffling is a no-op stub until a
/// shuffle-hook is plumbed through the engine (CR 701.19c).
/// </summary>
public static class OracleLandActivatedAbilityBinder
{
    // Matches: "{T}, Pay 1 life, Sacrifice <anything>: Search your library for a
    //           <Plains|Island|Swamp|Mountain|Forest> or <Plains|Island|Swamp|Mountain|Forest> card"
    private static readonly Regex FetchLand = new(
        @"\{T\}\s*,\s*Pay\s+1\s+life\s*,\s*Sacrifice\s+[^:]+:\s*Search\s+your\s+library\s+for\s+a\s+" +
        @"(?<a>Plains|Island|Swamp|Mountain|Forest)\s+or\s+(?<b>Plains|Island|Swamp|Mountain|Forest)\s+card",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Map from oracle name → CardSubtype enum value.
    private static readonly Dictionary<string, CardSubtype> SubtypeByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Plains"]   = CardSubtype.Plains,
            ["Island"]   = CardSubtype.Island,
            ["Swamp"]    = CardSubtype.Swamp,
            ["Mountain"] = CardSubtype.Mountain,
            ["Forest"]   = CardSubtype.Forest,
        };

    /// <summary>
    /// Inspect <paramref name="entity"/>'s oracle text and, if a fetch-land
    /// pattern is detected, attach the corresponding <see cref="ActivatedAbility"/>
    /// to <paramref name="card"/>. Does nothing if the card is not a
    /// <see cref="Land"/>.
    /// </summary>
    /// <returns><c>true</c> when an ability was attached; <c>false</c> otherwise.</returns>
    public static bool Bind(ICard card, CardEntity entity, Player controller)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        // Only bind to Land permanents.
        if (card is not Land land) return false;

        var text = entity.OracleText;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = FetchLand.Match(text);
        if (!m.Success) return false;

        var subtypeNameA = m.Groups["a"].Value;
        var subtypeNameB = m.Groups["b"].Value;

        if (!SubtypeByName.TryGetValue(subtypeNameA, out var subtypeA) ||
            !SubtypeByName.TryGetValue(subtypeNameB, out var subtypeB))
        {
            return false;
        }

        // Capture for closure — avoid capturing the match object.
        var fetchLand = land;
        var ctrl = controller;

        var ability = new ActivatedAbility(
            source: fetchLand,
            controller: ctrl,
            costs: new ICost[]
            {
                AdditionalCost.Tap(fetchLand),
                AdditionalCost.PayLife(1),
                AdditionalCost.Sacrifice(fetchLand),
            },
            effects: new IEffect[]
            {
                new Effect(
                    $"search library for {subtypeNameA} or {subtypeNameB} and put onto battlefield",
                    () => FetchEffect(ctrl, subtypeA, subtypeB)),
            });

        fetchLand.AddAbility(ability);
        return true;
    }

    private static void FetchEffect(Player controller, CardSubtype subtypeA, CardSubtype subtypeB)
    {
        // Search the library for the first card with either target land subtype.
        var target = controller.Zones.Library
            .GetCards()
            .FirstOrDefault(c => c.HasSubtype(subtypeA) || c.HasSubtype(subtypeB));

        if (target == null) return; // Nothing found — ability fizzles (library empty or no match).

        controller.Zones.Library.RemoveCard(target);
        controller.Zones.Battlefield.AddCard(target);
        // AddCard already calls target.SetZone(ZoneType.Battlefield) internally (Zone.AddCard).

        // CR 701.19c — "then shuffle." Shuffle hook not yet implemented; no-op stub.
    }
}
