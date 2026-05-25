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

    // ---------- Folding-pass patterns (PR-B) -------------------------------
    //
    // Two extra passes folded into NormalizeFolded so templates can opt into
    // a more aggressive view of the body. They are NOT applied by the
    // backwards-compatible Normalize entry-point.

    // A single mana symbol: a pip inside {…}. The grammar of a mana pip is
    // limited — basic colour (W/U/B/R/G/C), generic (digits or X/Y/Z),
    // snow (S), half-mana (H), hybrid (X/Y), Phyrexian (X/P). Tap "{T}",
    // untap "{Q}" and other non-mana brace markers are intentionally NOT
    // covered: they aren't mana costs, and folding them as "{cost}" would
    // mask abilities like "{T}: Add {G}." The character class is precise on
    // purpose so a future printing that introduces a non-mana glyph cannot
    // be silently swept into the fold.
    //
    // The pip body, before the closing brace, may be:
    //   * "\d+"        — generic mana ({2}, {15}).
    //   * Single colour W|U|B|R|G|C|X|Y|Z|S|H.
    //   * Hybrid "<X>/<Y>" where each side is one of the above (e.g. {W/U},
    //     {2/W}, {G/P}). Phyrexian is hybrid with the second side P.
    private const string ManaPipBody =
        @"(?:\d+|[WUBRGCXYZSH])(?:/[WUBRGCXYZSHP0-9])?";

    // One or more adjacent mana pips, whitespace allowed between (Scryfall
    // never inserts whitespace between adjacent pips, but tolerate it for
    // robustness). Anchoring on "two or more" would miss "{R}" payments;
    // we fold single pips too — a single mana payment is still a cost.
    private static readonly Regex ManaCostRun = new(
        @"\{" + ManaPipBody + @"\}(?:\s*\{" + ManaPipBody + @"\})*",
        RegexOptions.Compiled);

    // Standalone integer counts in the prose. Captured contexts:
    //   * P/T deltas:   +1/+1, -2/-0, +0/+3.
    //   * "N damage":   "deals 3 damage", "5 damage to".
    //   * "N card(s)":  "draw 2 cards", "discards 3 cards".
    //   * "N life":     "loses 4 life", "gain 2 life".
    //   * "N counter(s)" optional — covered by the +N/+N P/T case for
    //     creature pumps; cumulative counters of other kinds are folded by
    //     the explicit N-counter sub-pattern below.
    //
    // The folded view replaces each captured integer with the literal "n"
    // so templates that match on "deals n damage to any target" handle every
    // value of N. Templates that care about N's actual value MUST read it
    // from the un-folded ctx.Text (or ctx.RawText) using a separate scoped
    // regex; the fold is for matching, not for value extraction.

    // +N/+N or -N/-N power/toughness deltas (and 0/+N / +N/0 mixes).
    private static readonly Regex PtDelta = new(
        @"([+-])\d+/([+-])\d+",
        RegexOptions.Compiled);

    // "<verb> N <noun>" patterns. Each is anchored on a specific noun so the
    // fold doesn't accidentally swallow numbers in unrelated contexts
    // (e.g. "Choose 2 — Destroy target creature." — Scryfall doesn't write
    // this, but the regex is conservative regardless).
    //
    // Word-boundaries on both sides keep the digit standalone — multi-digit
    // costs like "10 damage" still fold to "n damage".
    private static readonly Regex NDamage = new(
        @"\b\d+\s+damage\b",
        RegexOptions.Compiled);
    private static readonly Regex NCards = new(
        @"\b\d+\s+cards?\b",
        RegexOptions.Compiled);
    private static readonly Regex NLife = new(
        @"\b\d+\s+life\b",
        RegexOptions.Compiled);
    private static readonly Regex NCounters = new(
        @"\b\d+\s+(?<kind>\+1/\+1|-1/-1|charge|loyalty|time|fade|age|verse)\s+counters?\b",
        RegexOptions.Compiled);

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

    /// <summary>
    /// PR-B: an additionally-folded view of the oracle body. Runs the same
    /// passes as <see cref="NormalizeForCard"/>, then folds two additional
    /// classes of trivial variant into stable tokens so a single template
    /// regex can match an entire family of cards:
    ///
    ///   1. Consecutive mana-cost pip runs collapse to the literal
    ///      "{cost}" token. Examples:
    ///        "{2}{W}{W}"   → "{cost}"
    ///        "{X}{R}"      → "{cost}"
    ///        "{T}, Pay {2}" → "{T}, Pay {cost}"  (Tap is NOT mana — left alone)
    ///
    ///   2. Standalone integer counts in a known noun context fold to the
    ///      literal "n" token. Examples:
    ///        "+1/+1"                 → "+n/+n"
    ///        "deals 3 damage"        → "deals n damage"
    ///        "draw 2 cards"          → "draw n cards"
    ///        "target opponent loses 4 life" → "target opponent loses n life"
    ///        "two +1/+1 counters"    → "two +n/+n counters" (delta first, then noun untouched)
    ///
    /// Lossy by design: the folded view loses the original numeric value.
    /// Templates that need the value match the folded text first (cheap
    /// reject), then run a scoped regex over the un-folded
    /// <see cref="SpellBindContext.Text"/> to pull out the actual digits.
    ///
    /// Backwards-compatible: existing templates that consult
    /// <see cref="SpellBindContext.Text"/> see the un-folded text and keep
    /// working unchanged. Templates that opt in read
    /// <see cref="SpellBindContext.TextFolded"/>.
    /// </summary>
    public static string NormalizeFolded(string text, string? cardName)
    {
        var s = NormalizeForCard(text, cardName);
        if (string.IsNullOrEmpty(s)) return s;
        return FoldTokens(s);
    }

    /// <summary>
    /// Applies the PR-B folding passes only — caller is responsible for
    /// running <see cref="Normalize"/> / <see cref="NormalizeForCard"/>
    /// first. Exposed for unit-test surface; production templates should
    /// reach for <see cref="NormalizeFolded"/> or
    /// <see cref="SpellBindContext.TextFolded"/>.
    /// </summary>
    public static string FoldTokens(string normalized)
    {
        if (string.IsNullOrEmpty(normalized)) return normalized;

        // Pass A: mana-cost run → {cost}. Done first because a run can
        // contain digits ({2}) that the numeric fold would otherwise eat
        // if it ran prior.
        var s = ManaCostRun.Replace(normalized, "{cost}");

        // Pass B: numeric folds. Each scoped regex is run in turn. Order
        // matters only between PT-delta (consumes "+1/+1") and the generic
        // N-counters pattern (uses "+1/+1" as its counter-kind anchor) —
        // we run delta last so the counter-kind anchor can still see the
        // raw "+1/+1" / "-1/-1". After NCounters folds the integer in
        // front of the counter kind, PtDelta then folds the counter-kind
        // P/T marker itself.
        s = NCounters.Replace(s, m => "n " + m.Groups["kind"].Value + (m.Value.EndsWith('s') ? " counters" : " counter"));
        s = PtDelta.Replace(s, m => m.Groups[1].Value + "n/" + m.Groups[2].Value + "n");
        s = NDamage.Replace(s, "n damage");
        s = NCards.Replace(s, m => m.Value.EndsWith('s') ? "n cards" : "n card");
        s = NLife.Replace(s, "n life");

        return s;
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
