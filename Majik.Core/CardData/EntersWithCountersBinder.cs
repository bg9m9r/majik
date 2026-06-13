using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;

namespace Majik.Core.CardData;

/// <summary>
/// CR 614.1d — detects "[Card] enters the battlefield with N +1/+1 counter(s)
/// on it" oracle text and registers an <see cref="EntersWithCountersReplacement"/>
/// on the supplied <see cref="ReplacementBus"/>.
///
/// Matched shapes:
///   * "enters with a +1/+1 counter on it"
///   * "enters with two +1/+1 counters on it"
///   * "enters the battlefield with three +1/+1 counters on it"
///   * "enters with X +1/+1 counters on it" (Walking Ballista, Endless One,
///     Hangarback Walker, …) — registers a DYNAMIC replacement that reads the
///     X chosen at cast time off <see cref="Card.PendingCastX"/> (CR 202.3b /
///     614.1d), so the permanent enters WITH the counters (no transient 0/0
///     window the SBA layer would immediately kill).
///
/// Conditional "with N counters" clauses ("for each Y, enters with a +1/+1
/// counter") stay unbound — they need a context-aware predicate.
/// </summary>
public static class EntersWithCountersBinder
{
    private static readonly Regex Pattern = new(
        @"\benters\s+(?:the\s+battlefield\s+)?with\s+(?<n>a|an|one|two|three|four|five|six|seven|eight|nine|ten|\d+)\s+\+1/\+1\s+counters?\s+on\s+it\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // CR 202.3b — variable "X +1/+1 counters" form. Separate from the fixed
    // Pattern so the X count is sourced dynamically from PendingCastX rather
    // than parsed to a literal. The "for each" guard in Bind excludes
    // conditional clauses that still need a context predicate.
    private static readonly Regex VariableXPattern = new(
        @"\benters\s+(?:the\s+battlefield\s+)?with\s+X\s+\+1/\+1\s+counters?\s+on\s+it\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool Bind(ICard card, CardEntity entity, ReplacementBus replacements)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (replacements == null) throw new ArgumentNullException(nameof(replacements));

        var text = entity.OracleText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = Pattern.Match(text);
        if (m.Success)
        {
            var n = WordToInt(m.Groups["n"].Value);
            if (n <= 0) return false;

            replacements.Register(new EntersWithCountersReplacement(card, n));
            return true;
        }

        // CR 202.3b — "enters with X +1/+1 counters on it". Skip the
        // conditional "for each …" shape (needs a context predicate, still
        // out of scope). Register a dynamic replacement that reads the chosen
        // X off PendingCastX at entry; non-cast entries (blink / copy / token)
        // have no stamp → X = 0 → no counters.
        var vx = VariableXPattern.Match(text);
        if (vx.Success
            // Scope the "for each" conditional-shape guard to the SENTENCE that
            // carries the matched "enters with X" clause — a card may carry an
            // unrelated "for each" elsewhere (Hangarback Walker's dies clause:
            // "create a … Thopter … for each +1/+1 counter"), which must NOT
            // disqualify its clean variable-X ETB clause.
            && !MatchSentenceHasForEach(text, vx)
            // CR 614.1d — skip when the card's factory ALREADY wires its own
            // X-counter mechanism; registering here would double the counters.
            && (card as Card)?.SelfManagesEntersWithCounters != true)
        {
            replacements.Register(new EntersWithCountersReplacement(
                card,
                CounterType.PlusOnePlusOne,
                () => (card as Card)?.PendingCastX ?? 0));
            return true;
        }

        return false;
    }

    // True when the SENTENCE containing the matched "enters with X +1/+1
    // counters on it" clause also contains "for each" — i.e. the conditional
    // "enters with … for each Y" shape that still needs a context predicate
    // (out of scope). Scoping to the sentence (bounded by sentence terminators
    // '.', '!', '?' and newlines) keeps an unrelated "for each" in a DIFFERENT
    // ability of the same card (e.g. Hangarback Walker's dies clause) from
    // disqualifying the clean variable-X ETB clause.
    private static bool MatchSentenceHasForEach(string text, Match match)
    {
        static bool IsBoundary(char c) => c is '.' or '!' or '?' or '\n' or '\r';

        var start = match.Index;
        while (start > 0 && !IsBoundary(text[start - 1])) start--;

        var end = match.Index + match.Length;
        while (end < text.Length && !IsBoundary(text[end])) end++;

        return text.IndexOf("for each", start, end - start, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int WordToInt(string s) =>
        s.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2, "three" => 3, "four" => 4, "five" => 5,
            "six" => 6, "seven" => 7, "eight" => 8, "nine" => 9, "ten" => 10,
            _ => int.TryParse(s, out var v) ? v : 0,
        };
}
