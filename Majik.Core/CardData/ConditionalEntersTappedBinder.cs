using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.1c — conditional "enters tapped unless …" binder. Detects the
/// "N or more / N or fewer other lands" variant used by Kamigawa channel
/// lands (Boseiju, Who Endures; Otawara, Soaring City; …) and the Murders
/// at Karlov Manor surveil-dual cycle (Underground Mortuary; …).
///
/// Other conditional variants — "unless you control a [subtype]" (Spymaster's
/// Vault, slow lands), "unless you control two or more basic lands" (check
/// lands), etc — need their own binders. The regex here only claims the
/// "N or [more|fewer] other lands" form.
/// </summary>
public static class ConditionalEntersTappedBinder
{
    // "enters tapped unless you revealed a <Subtype> card this way or you
    // control a <Subtype>." — Temple of the Dragon Queen ("…unless you revealed
    // a Dragon card this way or you control a Dragon"). The reveal half is an
    // "as this enters" decision (CR 614.10) resolved by a paired
    // RevealCardFromHandReplacement that stamps a shared flag; the control half
    // counts the controller's battlefield permanents carrying that subtype
    // (CR 205.3). Captured BEFORE the generic clauses so its richer shape isn't
    // mis-claimed.
    private static readonly Regex RevealedSubtypeOrControlClause = new(
        @"enters\s+(?:the\s+battlefield\s+)?tapped\s+unless\s+you\s+revealed\s+an?\s+(?<subtype>\w+)\s+card\s+this\s+way\s+or\s+you\s+control\s+an?\s+\w+",
        RegexOptions.IgnoreCase);

    // "enters tapped unless you control <count> or <more|fewer> other lands."
    // Captures the count word and the direction so the predicate can be
    // built generically.
    private static readonly Regex OtherLandsClause = new(
        @"enters\s+(?:the\s+battlefield\s+)?tapped\s+unless\s+you\s+control\s+(?<count>one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+or\s+(?<direction>more|fewer)\s+other\s+lands",
        RegexOptions.IgnoreCase);

    // "enters tapped unless you control <count> or more other <Subtype>s." —
    // the ELD "cottage" cycle (Witch's Cottage: "…three or more other Swamps";
    // Mystic Sanctuary: Islands; …). Subtype-count variant the generic
    // OtherLandsClause does NOT claim. Captures the count word and the basic
    // land subtype so the predicate counts that subtype on the battlefield.
    private static readonly Regex OtherSubtypeLandsClause = new(
        @"enters\s+(?:the\s+battlefield\s+)?tapped\s+unless\s+you\s+control\s+(?<count>one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+or\s+more\s+other\s+(?<subtype>Plains|Islands|Swamps|Mountains|Forests)",
        RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, CardSubtype> SubtypeByPlural =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Plains"]    = CardSubtype.Plains,
            ["Islands"]   = CardSubtype.Island,
            ["Swamps"]    = CardSubtype.Swamp,
            ["Mountains"] = CardSubtype.Mountain,
            ["Forests"]   = CardSubtype.Forest,
        };

    public static bool Bind(ICard card, CardEntity entity, ReplacementBus replacements)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));

        var text = entity.OracleText ?? string.Empty;

        // Reveal-or-control variant (Temple of the Dragon Queen): "enters tapped
        // unless you revealed a <Subtype> card this way or you control a
        // <Subtype>." CR 614.10 — the reveal is an "as this enters" may-decision;
        // CR 205.3 — Dragon is a creature type counted on the battlefield. Two
        // replacements are registered: a RevealCardFromHandReplacement that
        // prompts the controller's agent and stamps a shared flag (registered
        // FIRST so the flag is set before the tapped predicate runs), then a
        // ConditionalEntersTappedReplacement whose predicate is
        // (revealed-this-way OR control-a-Subtype).
        var revealMatch = RevealedSubtypeOrControlClause.Match(text);
        if (revealMatch.Success
            && ParseSubtype(revealMatch.Groups["subtype"].Value) is { } revealSubtype)
        {
            var flag = new RevealedFromHandFlag();
            var matchLabel = $"a {revealMatch.Groups["subtype"].Value} card";

            replacements.Register(
                new RevealCardFromHandReplacement(card, revealSubtype, matchLabel, flag));
            replacements.Register(new ConditionalEntersTappedReplacement(
                card,
                entersUntappedIf: (controller, self) =>
                    flag.Revealed || CountOtherSubtype(controller, self, revealSubtype) >= 1));
            return true;
        }

        // Subtype-count variant first (Witch's Cottage / the ELD cottage cycle):
        // "enters tapped unless you control N or more other <Subtype>s." CR 109.2
        // — "other" excludes the card itself; CR 305.6 — count permanents
        // carrying that basic land subtype (basic OR nonbasic).
        var subtypeMatch = OtherSubtypeLandsClause.Match(text);
        if (subtypeMatch.Success
            && SubtypeByPlural.TryGetValue(subtypeMatch.Groups["subtype"].Value, out var subtype))
        {
            var subtypeThreshold = WordToInt(subtypeMatch.Groups["count"].Value);
            replacements.Register(new ConditionalEntersTappedReplacement(
                card,
                entersUntappedIf: (controller, self) =>
                    CountOtherSubtype(controller, self, subtype) >= subtypeThreshold));
            return true;
        }

        var m = OtherLandsClause.Match(text);
        if (!m.Success) return false;

        var threshold = WordToInt(m.Groups["count"].Value);
        var direction = m.Groups["direction"].Value.ToLowerInvariant();

        // CR rule: "other" excludes the card itself. We exclude `self`
        // from the count so the replacement is correct whether evaluated
        // before or after the entering card has been placed on the
        // battlefield (ZoneService runs replacements before the move
        // commits, so the card isn't on the battlefield yet — but
        // future call sites may differ).
        Func<Player, ICard, bool> entersUntappedIf = direction switch
        {
            "more" => (controller, self) => CountOtherLands(controller, self) >= threshold,
            "fewer" => (controller, self) => CountOtherLands(controller, self) <= threshold,
            _ => (_, _) => false,
        };

        replacements.Register(new ConditionalEntersTappedReplacement(card, entersUntappedIf));
        return true;
    }

    private static int CountOtherLands(Player controller, ICard self) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasType(CardType.Land));

    private static int CountOtherSubtype(Player controller, ICard self, CardSubtype subtype) =>
        controller.Zones.Battlefield.GetCards()
            .Count(c => !ReferenceEquals(c, self) && c.HasSubtype(subtype));

    private static CardSubtype? ParseSubtype(string raw) =>
        Enum.TryParse<CardSubtype>(raw, ignoreCase: true, out var s) ? s : null;

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "one" => 1, "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var n) ? n : 0,
        };
}
