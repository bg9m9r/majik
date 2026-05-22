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
        // Delve (Each card you exile from your graveyard while casting this
        // spell pays for {1}.) — alt-cost keyword; v1 doesn't enforce, just
        // strips the reminder so the binder anchors on the effect.
        new(@"^\s*delve\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Bargain (You may sacrifice an artifact, enchantment, or token as
        // you cast this spell.) — Wilds of Eldraine cost-modifier keyword.
        // v1 stub: reminder stripped, "If this spell was bargained" clauses
        // (when present later in the text) are out of scope.
        new(@"^\s*bargain\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Affinity for X (This spell costs {1} less to cast for each X you
        // control.) — cost reduction keyword. Lossy: cost isn't reduced.
        // X is any word (artifacts, Allies, Frogs, …).
        new(@"^\s*affinity\s+for\s+\w+\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Suspend N—{cost} optionally followed by a (...) reminder. Time-
        // counter alt-cast keyword; v1 doesn't enforce exile/upkeep, the
        // effect just binds for normal casting. The em-dash is the Scryfall
        // canonical form; "{cost}" is one or more {…} groups.
        new(@"^\s*suspend\s+\d+\s*—\s*(?:\{[^}]+\}\s*)+(?:\([^)]*\)\s*)?", RegexOptions.IgnoreCase),
        // "As an additional cost to cast this spell, sacrifice/discard/exile X."
        // Drops the cost sentence — v1 stub doesn't enforce the additional
        // cost. Lossy: caster doesn't actually sacrifice anything but the
        // main effect still resolves.
        new(@"^\s*as\s+an\s+additional\s+cost\s+to\s+cast\s+this\s+spell,\s+[^.]+\.\s*", RegexOptions.IgnoreCase),
        // Generic leading parenthesized reminder text — Scryfall renders
        // some reminder paragraphs ahead of the real spell text (e.g.
        // Legendary sorcery's "(You may cast a legendary sorcery only if
        // you control a legendary creature or planeswalker.)"). Strip any
        // leading "(...)" reminder so the binder anchors on the effect.
        new(@"^\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
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
