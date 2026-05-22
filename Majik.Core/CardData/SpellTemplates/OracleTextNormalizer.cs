using System.Text.RegularExpressions;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Strips passive keyword markers from the leading edge of an oracle text so
/// the regex templates can anchor at the actual spell effect:
///
///   "Convoke (Your creatures can help cast this spell...) Destroy target …"
///       → "Destroy target …"
///   "Split second (As long as this spell is on the stack...) Counter target …"
///       → "Counter target …"
///   "Strive — This spell costs … more to cast for each target beyond the first.
///    Destroy target …"
///       → "Destroy target …"
///
/// The strip is line-by-line and purely cosmetic from the binder's standpoint;
/// the actual cost / timing semantics of these keywords aren't enforced at v1.
/// Lossy on purpose — Convoke spells still cost full mana, Split-Second spells
/// don't suppress responses, Strive spells don't reduce extra-target cost.
/// </summary>
public static class OracleTextNormalizer
{
    // Each pattern matches a leading passive-keyword prefix that should be
    // stripped before regex template matching. Patterns are anchored at the
    // start of text (or start of a line) and consume the full reminder/cost
    // sentence including trailing whitespace.
    private static readonly Regex[] LeadingPrefixes =
    {
        // Convoke (...) — the parenthesized reminder text right at the top.
        new(@"^\s*convoke\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Split second (...) — same shape.
        new(@"^\s*split\s+second\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Strive — This spell costs {X} more …
        new(@"^\s*strive\s+—\s+this\s+spell\s+costs\s+[^.]+\.\s*", RegexOptions.IgnoreCase),
        // Cipher reminder — Brought back as a placeholder; Cipher itself parsed
        // elsewhere. Cosmetic strip only.
        new(@"^\s*cipher\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // "As an additional cost to cast this spell, sacrifice/discard/exile X."
        // Drops the cost sentence — v1 stub doesn't enforce the additional
        // cost. Lossy: caster doesn't actually sacrifice anything but the
        // main effect still resolves.
        new(@"^\s*as\s+an\s+additional\s+cost\s+to\s+cast\s+this\s+spell,\s+[^.]+\.\s*", RegexOptions.IgnoreCase),
    };

    /// <summary>Strip recognised leading prefixes from <paramref name="text"/>.</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var s = text;
        // Loop until no more known prefix matches (cards rarely have more than
        // one of these, but Split-Second + Strive could in theory stack).
        for (var i = 0; i < 8; i++)
        {
            var changed = false;
            foreach (var rx in LeadingPrefixes)
            {
                var replaced = rx.Replace(s, "", count: 1);
                if (!ReferenceEquals(replaced, s) && replaced.Length != s.Length)
                {
                    s = replaced;
                    changed = true;
                }
            }
            if (!changed) break;
        }
        return s;
    }
}
