using System.Text.RegularExpressions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData;

/// <summary>
/// Synthesizes mana abilities from a card's TypeLine + OracleText:
///  - Basic lands (subtype Mountain/Forest/Plains/Island/Swamp) → fixed
///    tap-for-one-color, regardless of oracle text (Scryfall sometimes
///    omits the rules-text on a basic).
///  - Otherwise, scan oracle text for "{T}: Add {COLOR}." patterns —
///    handles Llanowar Elves, Mox, etc.
///
/// Limitations: ignores cost prefixes that aren't pure {T}, multi-color
/// modal mana ("Add one mana of any color"), and conditional triggers.
/// Those are deliberate gaps left for a richer binder.
/// </summary>
public static class OracleManaBinder
{
    private static readonly Dictionary<CardSubtype, string> BasicLandColors = new()
    {
        [CardSubtype.Mountain] = "R",
        [CardSubtype.Forest] = "G",
        [CardSubtype.Plains] = "W",
        [CardSubtype.Island] = "U",
        [CardSubtype.Swamp] = "B",
    };

    // {T}: Add {R}.  or  {T}: Add {R}{R}.  or  {T}: Add one {G}.
    private static readonly Regex TapForManaRegex = new(
        @"\{T\}\s*:\s*Add\s+((?:\{[WUBRGC]\}\s*)+)",
        RegexOptions.IgnoreCase);

    public static void Bind(ICard card, CardEntity entity, Player controller)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (controller == null) throw new ArgumentNullException(nameof(controller));

        if (TryBindBasicLand(card, controller)) return;
        TryBindFromOracle(card, entity, controller);
    }

    private static bool TryBindBasicLand(ICard card, Player controller)
    {
        if (!card.HasSupertype(CardSupertype.Basic)) return false;

        foreach (var (subtype, color) in BasicLandColors)
        {
            if (card.HasSubtype(subtype))
            {
                card.AddAbility(new ManaAbility(card, controller, ManaCost.Parse(color)));
                return true;
            }
        }
        return false;
    }

    private static void TryBindFromOracle(ICard card, CardEntity entity, Player controller)
    {
        var text = entity.OracleText;
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (Match m in TapForManaRegex.Matches(text))
        {
            var symbols = m.Groups[1].Value;
            var stripped = symbols.Replace("{", "").Replace("}", "").Replace(" ", "");
            if (string.IsNullOrEmpty(stripped)) continue;

            card.AddAbility(new ManaAbility(card, controller, ManaCost.Parse(stripped)));
        }
    }
}
