using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.ValueObjects;

namespace Majik.Core.Rules;

/// <summary>
/// CR 702.16 helpers — answer "does this permanent have protection from
/// X?". Quality strings on <see cref="ProtectionAbility"/> are matched
/// case-insensitively. Common buckets:
///   - colour: "white" "blue" "black" "red" "green" "all colors"
///   - type: "creatures" "artifacts" "enchantments" "lands"
///           "planeswalkers" "instants" "sorceries"
///   - name: any other free-form string
/// </summary>
public static class Protection
{
    public static bool HasProtectionFromColor(ICard card, ManaColor color)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        var name = color switch
        {
            ManaColor.White => "white",
            ManaColor.Blue => "blue",
            ManaColor.Black => "black",
            ManaColor.Red => "red",
            ManaColor.Green => "green",
            _ => "",
        };
        return Qualities(card).Any(q =>
            q == name || q == "all colors" || q == "everything");
    }

    public static bool HasProtectionFromCardType(ICard card, CardType type)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        var plural = type switch
        {
            CardType.Creature => "creatures",
            CardType.Artifact => "artifacts",
            CardType.Enchantment => "enchantments",
            CardType.Land => "lands",
            CardType.Planeswalker => "planeswalkers",
            CardType.Instant => "instants",
            CardType.Sorcery => "sorceries",
            _ => "",
        };
        return Qualities(card).Any(q => q == plural);
    }

    private static IEnumerable<string> Qualities(ICard card) =>
        card.Abilities.OfType<ProtectionAbility>().Select(p => p.Quality);
}
