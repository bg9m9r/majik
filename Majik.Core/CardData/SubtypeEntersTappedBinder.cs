using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.1c — "enters tapped unless you control a [subtype]" binder.
/// Covers Spymaster's Vault (Swamp), Drowned Catacomb / Sulfur Falls /
/// Hinterland Harbor (the Ixalan + Innistrad check-land cycles take
/// "a [Subtype1] or a [Subtype2]" — handled below), and similar cards
/// that gate their ETB on the controller already owning a permanent of
/// a specific subtype.
///
/// Patterns matched (case-insensitive):
///   - "enters tapped unless you control a &lt;subtype&gt;"
///   - "enters tapped unless you control a &lt;subtype1&gt; or a &lt;subtype2&gt;"
///   - "enters tapped unless you control an &lt;subtype&gt;"   (article variant)
///
/// Subtype words are mapped through <see cref="ParseSubtype"/>; words that
/// don't resolve to a known <see cref="CardSubtype"/> cause the binder to
/// skip (binding returns false). The card itself is excluded from the
/// "you control a [subtype]" check via reference equality.
/// </summary>
public static class SubtypeEntersTappedBinder
{
    // First alternative: two-subtype form ("a X or a Y"). Captured before
    // the single-subtype form so the "or" tail isn't lost.
    private static readonly Regex TwoSubtypeClause = new(
        @"enters\s+(?:the\s+battlefield\s+)?tapped\s+unless\s+you\s+control\s+an?\s+(?<a>\w+)\s+or\s+an?\s+(?<b>\w+)",
        RegexOptions.IgnoreCase);

    private static readonly Regex SingleSubtypeClause = new(
        @"enters\s+(?:the\s+battlefield\s+)?tapped\s+unless\s+you\s+control\s+an?\s+(?<a>\w+)\b",
        RegexOptions.IgnoreCase);

    public static bool Bind(ICard card, CardEntity entity, ReplacementBus replacements)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));

        var text = entity.OracleText ?? string.Empty;

        var two = TwoSubtypeClause.Match(text);
        if (two.Success)
        {
            var a = ParseSubtype(two.Groups["a"].Value);
            var b = ParseSubtype(two.Groups["b"].Value);
            if (a is null || b is null) return false;

            var subA = a.Value;
            var subB = b.Value;
            replacements.Register(new ConditionalEntersTappedReplacement(card,
                (controller, self) => ControllerHasSubtype(controller, self, subA)
                                   || ControllerHasSubtype(controller, self, subB)));
            return true;
        }

        var single = SingleSubtypeClause.Match(text);
        if (single.Success)
        {
            var s = ParseSubtype(single.Groups["a"].Value);
            if (s is null) return false;

            var sub = s.Value;
            replacements.Register(new ConditionalEntersTappedReplacement(card,
                (controller, self) => ControllerHasSubtype(controller, self, sub)));
            return true;
        }

        return false;
    }

    private static bool ControllerHasSubtype(
        Majik.Core.Players.Player controller,
        ICard self,
        CardSubtype subtype) =>
        controller.Zones.Battlefield.GetCards()
            .Any(c => !ReferenceEquals(c, self) && c.HasSubtype(subtype));

    private static CardSubtype? ParseSubtype(string raw) =>
        Enum.TryParse<CardSubtype>(raw, ignoreCase: true, out var s) ? s : null;
}
