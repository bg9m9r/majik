using System.Text.Json;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData;

/// <summary>
/// Reads the Scryfall <see cref="CardEntity.Keywords"/> JSON array and
/// attaches a <see cref="KeywordAbility"/> for each evergreen keyword the
/// engine knows how to act on (currently: combat-relevant + Flash). Non-
/// evergreen / mechanically-complex keywords (Storm, Suspend, Kicker, …)
/// are ignored here and would need bespoke binders.
/// </summary>
public static class KeywordBinder
{
    /// <summary>
    /// Evergreen / runtime-handled keywords. Anything not in this set is
    /// dropped on the floor (the card still loads — just without that
    /// keyword's behavior).
    /// </summary>
    private static readonly HashSet<string> Recognized = new(StringComparer.OrdinalIgnoreCase)
    {
        "Flying", "Trample", "Vigilance", "Haste",
        "First strike", "Double strike",
        "Deathtouch", "Lifelink", "Reach", "Menace",
        "Defender", "Indestructible", "Flash",
    };

    public static void Bind(ICard card, CardEntity entity, Player controller)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        var keywords = ParseKeywordsArray(entity.Keywords);
        foreach (var kw in keywords)
        {
            if (Recognized.Contains(kw))
            {
                card.AddAbility(new KeywordAbility(kw, card, controller));
            }
        }
    }

    private static IReadOnlyList<string> ParseKeywordsArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return Array.Empty<string>();
        }

        try
        {
            var result = JsonSerializer.Deserialize<List<string>>(json);
            return (IReadOnlyList<string>?)result ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
