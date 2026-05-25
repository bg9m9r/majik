using System.Text.RegularExpressions;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Strips passive keyword markers and reminder text from oracle text so the
/// regex templates can anchor at the actual spell effect:
///
///   "Convoke (Your creatures can help cast this spell...) Destroy target …"
///       → "Destroy target …"
///   "Split second (As long as this spell is on the stack...) Counter target …"
///       → "Counter target …"
///   "Strive — This spell costs … more to cast for each target beyond the first.
///    Destroy target …"
///       → "Destroy target …"
///
/// The strip is purely cosmetic from the binder's standpoint; the actual cost
/// / timing semantics of these keywords aren't enforced at v1. Lossy on
/// purpose — Convoke spells still cost full mana, Split-Second spells don't
/// suppress responses, Strive spells don't reduce extra-target cost.
///
/// Additional passes (added incrementally):
///   * After the leading-prefix loop, ANY parenthesized reminder text is
///     stripped from the body (MTG oracle text never uses parens
///     semantically — only for reminders).
///   * En-dash "–" / horizontal-bar "―" / double-hyphen "--" → em-dash "—"
///     (Scryfall canonical).
///   * Curly quotes → straight ASCII quotes.
///   * Multiple whitespace (incl. newlines) → single spaces. Leading /
///     trailing whitespace trimmed.
///   * <see cref="NormalizeForCard"/> additionally replaces the card's own
///     name (and the pre-comma "short" form) with the Scryfall "~" sentinel
///     so templates can match on the placeholder rather than the literal.
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
        // Casualty N (As you cast this spell, you may sacrifice a creature
        // with power N or greater. When you do, copy this spell.) —
        // Streets of New Capenna additional-cost keyword. v1 stub.
        new(@"^\s*casualty\s+\d+\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Demonstrate (When you cast this spell, you may copy it. If you do,
        // choose an opponent to also copy it.) — Strixhaven keyword.
        new(@"^\s*demonstrate\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Disturb {cost} (...) — Innistrad: Midnight Hunt back-side cast
        // alt-cost. Reminder optional.
        new(@"^\s*disturb\s+(?:\{[^}]+\}\s*)+(?:\([^)]*\)\s*)?", RegexOptions.IgnoreCase),
        // Foretell {cost} (...) — Kaldheim exile-from-hand alt-cost.
        new(@"^\s*foretell\s+(?:\{[^}]+\}\s*)+(?:\([^)]*\)\s*)?", RegexOptions.IgnoreCase),
        // Replicate {cost} (...) — Dissension additional cost to copy.
        new(@"^\s*replicate\s+(?:\{[^}]+\}\s*)+(?:\([^)]*\)\s*)?", RegexOptions.IgnoreCase),
        // Buyback {cost} (...) — Tempest alt-additional cost.
        new(@"^\s*buyback\s+(?:\{[^}]+\}\s*)+(?:\([^)]*\)\s*)?", RegexOptions.IgnoreCase),
        // Storm (When you cast this spell, copy it for each spell cast
        // before it this turn.) — Scourge keyword. Standalone reminder.
        new(@"^\s*storm\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Overload {cost} (...) — Return to Ravnica alt-cast.
        new(@"^\s*overload\s+(?:\{[^}]+\}\s*)+(?:\([^)]*\)\s*)?", RegexOptions.IgnoreCase),
        // Spree (Choose one or more additional costs.) — Outlaws of Thunder
        // Junction modal additional-cost shape. Scryfall renders the
        // keyword as "Spree" followed by a parenthesized reminder.
        new(@"^\s*spree\s*\([^)]*\)\s*", RegexOptions.IgnoreCase),
        // Surge {cost} (...) — Oath of the Gatewatch alt-cost when a
        // teammate has cast a spell this turn.
        new(@"^\s*surge\s+(?:\{[^}]+\}\s*)+(?:\([^)]*\)\s*)?", RegexOptions.IgnoreCase),
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

    // Strip ALL parenthesized reminder runs anywhere in the body. MTG
    // oracle text never uses parens semantically — they're always
    // reminders ("Trample (this can deal excess damage...)"). Run after
    // the leading-prefix loop so prefix anchoring isn't disturbed.
    private static readonly Regex AnyParenReminder = new(@"\s*\([^)]*\)\s*", RegexOptions.Compiled);

    // Collapse any run of whitespace (incl. newlines) to a single space.
    private static readonly Regex MultiWhitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Strip recognised leading prefixes from <paramref name="text"/> plus
    /// the additional cleanups described on the type. Does NOT substitute
    /// the card's name — see <see cref="NormalizeForCard"/> for that.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var s = text;

        // Pass 1: curly quotes → ASCII. Done early so any subsequent regex
        // pattern can match on plain ASCII quote characters.
        s = FoldQuotes(s);

        // Pass 2: dash normalization (Scryfall canonical is em-dash). Done
        // before prefix matching so patterns like Strive's "Strive — …"
        // also fire for en-dash / double-hyphen / horizontal-bar variants
        // that some data sources emit. Order matters: the literal "--"
        // pair has to be rewritten before single-char replacements so it
        // collapses into a single em-dash, not two.
        s = s.Replace("--", "—");
        s = s.Replace('–', '—'); // en-dash → em-dash
        s = s.Replace('―', '—'); // horizontal bar → em-dash

        // Pass 3: known leading prefixes (looped — Split-Second + Strive
        // could in theory stack).
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

        // Pass 4: strip every parenthesized reminder remaining in the body.
        // Replace with a single space so adjacent words don't get glued.
        s = AnyParenReminder.Replace(s, " ");

        // Pass 5: whitespace collapse + trim.
        s = MultiWhitespace.Replace(s, " ").Trim();

        return s;
    }

    /// <summary>
    /// Same as <see cref="Normalize"/> but additionally replaces the card's
    /// own name (and its pre-comma short form, e.g. "Bonecrusher Giant" for
    /// "Bonecrusher Giant, Stomp") with the Scryfall "~" placeholder before
    /// any other passes run. Matching is case-insensitive and word-boundary
    /// anchored so a short name fragment never shreds a longer English word.
    ///
    /// Templates that anchor on "~" can stay name-agnostic and still match
    /// cards whose oracle text repeats the card's name (e.g. "Lightning
    /// Bolt deals 3 damage to any target.").
    /// </summary>
    public static string NormalizeForCard(string text, string? cardName)
    {
        if (string.IsNullOrEmpty(text)) return Normalize(text);
        if (string.IsNullOrWhiteSpace(cardName)) return Normalize(text);
        // Single-character names are skipped — real MTG card names are
        // never one character long, and test fixtures commonly use "X" as
        // a stand-in name. Word-boundary regex would otherwise rewrite
        // every standalone "X" (variable mana, X-cost spells) in the body.
        if (cardName!.Trim().Length < 2) return Normalize(text);

        var s = ReplaceCardName(text, cardName);
        return Normalize(s);
    }

    private static string ReplaceCardName(string text, string cardName)
    {
        // Fold curly quotes in both the body and the name BEFORE matching
        // so a name like "Urza's Saga" still matches body text that uses
        // a curly apostrophe (and vice versa).
        var body = FoldQuotes(text);
        var name = FoldQuotes(cardName);

        // Replace the full printed name first (longest match wins so we
        // don't shadow it with the shorter pre-comma form).
        var s = ReplaceLiteralIgnoreCase(body, name, "~");

        // Some cards print their name with a ", Subtitle" suffix (e.g.
        // "Bonecrusher Giant, Stomp", or "Lim-Dûl's Vault"-style suffix).
        // Also replace the part before the first comma if it differs.
        var commaIdx = name.IndexOf(',');
        if (commaIdx > 0)
        {
            var shortName = name[..commaIdx].Trim();
            if (shortName.Length > 0 && !string.Equals(shortName, name, StringComparison.Ordinal))
            {
                s = ReplaceLiteralIgnoreCase(s, shortName, "~");
            }
        }

        return s;
    }

    private static string FoldQuotes(string s) => s
        .Replace('‘', '\'')
        .Replace('’', '\'')
        .Replace('“', '"')
        .Replace('”', '"');

    private static string ReplaceLiteralIgnoreCase(string source, string needle, string replacement)
    {
        if (string.IsNullOrEmpty(needle)) return source;
        // Word-boundary anchored so single-letter or short test fixture
        // names ("X", "Bolt") don't shred normal English words ("eXile",
        // "Bolt" inside "thunderBolt", etc.). \b doesn't fire next to a
        // unicode apostrophe or curly quote — by this point quotes have
        // been folded to ASCII, so \b is safe.
        var pattern = $@"\b{Regex.Escape(needle)}\b";
        return Regex.Replace(source, pattern, replacement, RegexOptions.IgnoreCase);
    }
}
