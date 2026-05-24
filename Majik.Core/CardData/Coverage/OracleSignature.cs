using System.Text;
using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;

namespace Majik.Core.CardData.Coverage;

/// <summary>
/// Pure, deterministic oracle-text -> signature pipeline used by the
/// <c>coverage-gaps</c> subcommand to bucket Unimplemented cards into
/// mechanic clusters.
///
/// Three signatures are produced per card:
/// <list type="bullet">
///   <item><see cref="FirstSentenceSignature"/> — first sentence with
///   numbers, costs, and the card's name normalized away. The primary
///   clustering key.</item>
///   <item><see cref="TriggerSignature"/> — the opening clause up to the
///   first comma (when ~ enters, / at the beginning of …, / whenever …,).
///   Coarser bucket used for cross-cluster grouping.</item>
///   <item><see cref="EffectVerbSignature"/> — the dominant verb phrase
///   from the resolve clause: "deal damage" / "draw cards" / "destroy
///   target" / etc. Coarsest bucket, for top-level rollups.</item>
/// </list>
/// No I/O. No reflection. Safe to call millions of times.
/// </summary>
public sealed record OracleSignature(
    string Normalized,
    string FirstSentenceSignature,
    string TriggerSignature,
    string EffectVerbSignature)
{
    private static readonly Regex ReminderTextRx =
        new(@"\([^)]*\)", RegexOptions.Compiled);

    private static readonly Regex ManaSymbolRunRx =
        new(@"(?:\{[^}]+\})+", RegexOptions.Compiled);

    private static readonly Regex PowerToughnessRx =
        new(@"[+-]\d+/[+-]\d+", RegexOptions.Compiled);

    private static readonly Regex StandaloneIntRx =
        new(@"(?<![\w/])-?\d+(?![\w/])", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRx =
        new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Words we'll treat as "card-self-references" when none of the
    /// derived short-name forms hit. Used to canonicalise oracle text
    /// where the printed name was stripped at Scryfall (rare).
    /// </summary>
    private static readonly string[] SelfPronouns =
        { "this creature", "this permanent", "this card" };

    /// <summary>
    /// Build a signature triple for a given card entity. The card's name
    /// (and a derived short-name — first comma-separated piece, or first
    /// space-separated word for legendary names) is replaced with the
    /// sentinel <c>~</c>.
    /// </summary>
    public static OracleSignature From(CardEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var raw = entity.OracleText ?? "";
        return From(entity.Name, raw);
    }

    /// <summary>Test seam — pass name and oracle text directly.</summary>
    public static OracleSignature From(string name, string oracleText)
    {
        var normalized = Normalize(name, oracleText);
        var firstSentence = FirstSentence(normalized);
        var trigger = TriggerClause(firstSentence);
        var verb = EffectVerb(normalized);
        return new OracleSignature(normalized, firstSentence, trigger, verb);
    }

    /// <summary>
    /// Lowercase, strip reminder text, replace the card's name with the
    /// <c>~</c> sentinel, collapse mana-symbol runs to <c>{cost}</c>,
    /// canonicalise +N/+N power/toughness deltas and standalone integers
    /// to <c>N</c>, collapse whitespace.
    /// </summary>
    public static string Normalize(string name, string oracleText)
    {
        if (string.IsNullOrWhiteSpace(oracleText)) return "";

        var text = oracleText.Replace("\r", "").ToLowerInvariant();

        // 1) Strip parenthesised reminder text (everything Scryfall puts
        // in italics — "(this creature can't be blocked …)").
        text = ReminderTextRx.Replace(text, " ");

        // 2) Replace the card's full name and its derived short forms
        // with the ~ sentinel. Long form first so partial matches don't
        // shadow it.
        var fullName = (name ?? "").ToLowerInvariant().Trim();
        if (fullName.Length > 0)
        {
            text = text.Replace(fullName, "~");

            var shortName = DeriveShortName(fullName);
            if (!string.IsNullOrEmpty(shortName) && shortName != fullName)
            {
                text = text.Replace(shortName, "~");
            }
        }

        // 3) Self-reference phrases — only as a fallback for cards where
        // Scryfall already replaced the name with a pronoun. Done last
        // because it can shadow the name's first token.
        foreach (var pronoun in SelfPronouns)
        {
            // Only collapse when alone — don't rewrite "this creature's
            // controller" into "~'s controller" (we'd lose the noun).
            text = Regex.Replace(text, $@"\b{Regex.Escape(pronoun)}\b", "~");
        }

        // 4) Mana cost runs → {cost}. "{2}{R}{R}" → "{cost}".
        text = ManaSymbolRunRx.Replace(text, "{cost}");

        // 5) Power/toughness modifiers → +n/+n (preserves the +/- shape
        // so "+1/+1 counter" and "-1/-1 counter" don't collapse). Lower-
        // case n so the whole normalized string stays single-case.
        text = PowerToughnessRx.Replace(text, m =>
        {
            var sign = m.Value[0];
            return $"{sign}n/{sign}n";
        });

        // 6) Standalone integers → n. (Already-rewritten +n/+n etc. are
        // safe because the digits are gone.)
        text = StandaloneIntRx.Replace(text, "n");

        // 7) Collapse whitespace.
        text = WhitespaceRx.Replace(text, " ").Trim();

        return text;
    }

    private static string DeriveShortName(string fullName)
    {
        // "Llanowar Elves" → "llanowar elves" (no short form, returns
        // self — caller will detect and skip).
        // "Urza, Lord High Artificer" → "urza"
        // "Atraxa, Praetors' Voice" → "atraxa"
        var commaIdx = fullName.IndexOf(',');
        if (commaIdx > 0) return fullName.Substring(0, commaIdx).Trim();
        return fullName;
    }

    private static string FirstSentence(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return "";
        // Split on '.' but not on the '.' inside number tokens — we've
        // already normalised those away, so a plain split is safe.
        var idx = normalized.IndexOf('.');
        var sentence = idx < 0 ? normalized : normalized.Substring(0, idx);
        return sentence.Trim();
    }

    private static string TriggerClause(string firstSentence)
    {
        if (string.IsNullOrWhiteSpace(firstSentence)) return "";

        // Static-ability shapes ("flying", "{cost}: do X") don't have a
        // trigger-trigger; their cluster bucket should be the first
        // sentence itself, so report empty here.
        var lower = firstSentence.TrimStart();

        // Activated abilities — opening cost clause. Use the colon as
        // the trigger boundary so "{cost}: ~ deals N damage…" buckets
        // as "{cost}:".
        var colonIdx = lower.IndexOf(':');
        if (colonIdx > 0)
        {
            var commaIdxInside = lower.IndexOf(',');
            if (commaIdxInside < 0 || commaIdxInside > colonIdx)
            {
                return lower.Substring(0, colonIdx + 1).Trim();
            }
        }

        // Triggered abilities — opening up to first comma.
        var commaIdx = lower.IndexOf(',');
        if (commaIdx > 0)
        {
            var head = lower.Substring(0, commaIdx).Trim();
            // Heuristic: "when ~ enters" / "at the beginning of …" /
            // "whenever …" — must start with one of those tokens to be
            // a trigger. Otherwise leave empty.
            if (head.StartsWith("when ") || head.StartsWith("whenever ") ||
                head.StartsWith("at the beginning of ") || head.StartsWith("at end of "))
            {
                return head + ",";
            }
        }

        return "";
    }

    /// <summary>
    /// Best-effort verb phrase extractor. Returns the canonical effect
    /// verb if one of a short whitelist appears in the text; otherwise
    /// returns the empty string. The list is deliberately small — we'd
    /// rather miss a verb than mis-bucket. Add to this catalog when a
    /// new high-volume mechanic surfaces in cluster output.
    /// </summary>
    public static string EffectVerb(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return "";

        foreach (var (token, label) in EffectVerbCatalog)
        {
            if (normalized.Contains(token, StringComparison.Ordinal))
            {
                return label;
            }
        }
        return "";
    }

    /// <summary>
    /// Ordered (most-specific first) catalog of substring → canonical
    /// verb label. Public so the markdown writer can label clusters
    /// without reaching back into the regex code.
    /// </summary>
    public static readonly IReadOnlyList<(string Probe, string Label)> EffectVerbCatalog =
        new (string, string)[]
        {
            ("deals n damage to any target", "deal damage (any target)"),
            ("deals n damage to target creature or player", "deal damage (creature or player)"),
            ("deals n damage to target player", "deal damage (player)"),
            ("deals n damage to target creature", "deal damage (creature)"),
            ("deals n damage to each opponent", "deal damage (each opponent)"),
            ("deals n damage to each creature", "deal damage (each creature)"),
            ("deals n damage", "deal damage"),
            ("draw n cards", "draw cards"),
            ("draw a card", "draw a card"),
            ("destroy target creature", "destroy target creature"),
            ("destroy target nonland permanent", "destroy nonland permanent"),
            ("destroy all creatures", "destroy all creatures"),
            ("destroy all", "destroy all"),
            ("destroy target", "destroy target"),
            ("exile target creature", "exile target creature"),
            ("exile target", "exile target"),
            ("counter target spell", "counter spell"),
            ("counter target", "counter target"),
            ("return target", "return target"),
            ("return to its owner's hand", "bounce to hand"),
            ("gain n life", "gain life"),
            ("gains n life", "gain life"),
            ("lose n life", "lose life"),
            ("loses n life", "lose life"),
            ("mills n cards", "mill cards"),
            ("create n", "create token"),
            ("create a", "create token"),
            ("put n +n/+n counter", "+N/+N counter"),
            ("get +n/+n until end of turn", "pump (+N/+N EOT)"),
            ("gets +n/+n until end of turn", "pump (+N/+N EOT)"),
            ("tap target", "tap target"),
            ("untap target", "untap target"),
            ("search your library", "tutor"),
            ("scry n", "scry"),
            ("surveil n", "surveil"),
        };

    /// <summary>
    /// Build a human-readable display form of <paramref name="signature"/>
    /// for console / markdown output. Caps length and replaces newlines.
    /// </summary>
    public static string ToDisplay(string signature, int maxLen = 120)
    {
        if (string.IsNullOrEmpty(signature)) return "(none)";
        var s = signature.Replace('\n', ' ').Trim();
        if (s.Length > maxLen) s = s.Substring(0, maxLen - 1) + "…";
        return s;
    }

    /// <summary>
    /// Build a short-form preview of the raw oracle text (post-reminder
    /// stripping, pre-normalization). Used in markdown for canonical
    /// example display so the reader sees actual card text, not the
    /// tokenised signature.
    /// </summary>
    public static string PreviewOracle(string? oracleText, int maxLen = 400)
    {
        if (string.IsNullOrWhiteSpace(oracleText)) return "";
        var stripped = ReminderTextRx.Replace(oracleText, " ");
        stripped = WhitespaceRx.Replace(stripped, " ").Trim();
        if (stripped.Length > maxLen) stripped = stripped.Substring(0, maxLen - 1) + "…";
        return stripped;
    }
}
