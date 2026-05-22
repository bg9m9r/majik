using System.Text.Json;
using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Multi-clause spell composer (Priority 200). Splits oracle text on
/// '.', binds each clause via the rest of the registry, and composes a
/// single <see cref="SpellDefinition"/> from the per-clause results.
///
/// Runs BEFORE single-template binds so a multi-clause card like
/// "Return target creature to its owner's hand. Draw a card." resolves
/// to BOTH effects instead of collapsing to whichever single-template
/// matched first. Single-clause cards fall through cleanly since the
/// composer requires 2+ bound clauses.
///
/// All-or-nothing: every non-noop clause must bind, else this template
/// returns null and the card falls through to single-template binding.
///
/// Noop clauses (riders that v1 doesn't model — "It can't be
/// regenerated", "This damage can't be prevented", "Exile [self]") are
/// dropped before the all-bind check. Reminder text in parentheses is
/// stripped globally.
///
/// Opted into pre-compilation: <see cref="TryExtractParams"/> serializes
/// the per-clause {template name, sub-params} list as JSON;
/// <see cref="Rehydrate"/> deserializes and rebuilds via each
/// sub-template's own <c>Rehydrate</c>. CompiledSpellTemplates stores
/// the JSON under the single key "clauses".
/// </summary>
public sealed class ClauseCompositionTemplate : ISpellTemplate
{
    private static readonly Regex ReminderText = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex[] NoopClauses = new[]
    {
        // Regeneration / prevention bypass riders (creature kill spells).
        new Regex(@"^(it|they|that\s+creature|those\s+creatures)\s+can'?t\s+be\s+regenerated$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^this\s+damage\s+can'?t\s+be\s+prevented",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^the\s+damage\s+can'?t\s+be\s+prevented",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^this\s+spell\s+can'?t\s+be\s+countered\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Keyword-ability declarations on instants / sorceries. The cast-time
        // mechanics (alternate costs, recurrence, copy hooks) are evaluated
        // by the keyword pipeline elsewhere; the appearance of the keyword
        // as a clause inside oracle text doesn't add a resolution-time effect.
        new Regex(@"^cycling\s+\{",       RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^flashback\s+\{",     RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^retrace$",           RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^rebound$",           RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^storm$",             RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^cipher$",            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^jump-start$",        RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^conspire$",          RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^learn$",             RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^entwine\s+\{",       RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^buyback\s+\{",       RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^madness\s+\{",       RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^kicker\s+\{",        RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^multikicker\s+\{",   RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^splice\s+",          RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^suspend\s+",         RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^foretell\s+\{",      RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^the\s+ring\s+tempts\s+you$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^spell\s+mastery",    RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^domain\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^morbid\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^delirium\b",         RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^revolt\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^delve\b",            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^convoke\b",          RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^awaken\s+\d+",       RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^bargain\b",          RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^escalate\s*[—\{]",   RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Copy-spell pickers — riders that don't add a resolution effect.
        new Regex(@"^you\s+may\s+choose\s+new\s+targets?\s+for\s+the\s+(?:copy|copies)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^you\s+may\s+choose\s+the\s+same\s+mode\s+more\s+than\s+once",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Search/library plumbing bundled with tutoring effects.
        new Regex(@"^then\s+shuffle$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^then\s+that\s+player\s+shuffles$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^if\s+you\s+search\s+your\s+library\s+this\s+way,?\s*shuffle$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Pile-tail clauses from look-at-top variants.
        new Regex(@"^put\s+the\s+rest\s+(?:on\s+the\s+bottom\s+of\s+your\s+library|into\s+your\s+graveyard)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Exile-on-death/counter replacement riders (Pillar of Flame style).
        // Lossy at v1 — main effect still resolves; replacement effect
        // does not register.
        new Regex(@"^if\s+(?:that\s+creature|a\s+creature\s+dealt\s+damage\s+this\s+way)\s+would\s+die\s+this\s+turn,?\s+exile\s+it\s+instead",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^if\s+that\s+spell\s+is\s+countered\s+this\s+way,?\s+exile\s+it\s+instead",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Anaphoric riders referencing the previous clause's target.
        new Regex(@"^untap\s+(?:it|that\s+creature|that\s+permanent|those\s+creatures|that\s+artifact)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^(?:it|they|that\s+creature|those\s+creatures)\s+gains?\s+haste\s+until\s+end\s+of\s+turn",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^exile\s+(?:it|that\s+creature|that\s+permanent)\s+at\s+the\s+beginning\s+of\s+the\s+next\s+end\s+step",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Choose-mode preamble. Per-mode bullets still bind separately.
        new Regex(@"^choose\s+(?:one|two|three|one\s+or\s+more)(?:\s*—)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Cast-time additional-cost riders. Cost enforced at cast, not
        // at resolution.
        new Regex(@"^as\s+an\s+additional\s+cost\s+to\s+cast\s+this\s+spell",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Combat-damage prevention riders.
        new Regex(@"^prevent\s+all\s+combat\s+damage\s+that\s+would\s+be\s+dealt\s+this\s+turn",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // "Look at the top N cards of your library" as a bare clause —
        // when the head of an Impulse-style sequence already binds via
        // LookAtTopPutOneInHand, the bare look-clause that some cards
        // emit (e.g. Telling Time's secondary clauses) is a no-op
        // because the actual pile manipulation lives in a later clause
        // we can't safely model yet.
        new Regex(@"^look\s+at\s+the\s+top\s+(?:\d+|x|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?\s+of\s+your\s+library$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // "Then ..." continuation clauses (search-tutor closure phrases
        // bundled with library searches we already bind).
        new Regex(@"^then\s+shuffle\s+your\s+library$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Clash mechanic — affects nothing at resolution v1.
        new Regex(@"^clash\s+with\s+an\s+opponent$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Cast-time-only restrictions and self-referential preambles.
        new Regex(@"^cast\s+this\s+spell\s+only\s+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^you\s+may\s+cast\s+this\s+(?:spell|card)\s+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Coercion-trio clauses (reveal + pick + discard) — anaphoric;
        // each clause individually is information-only at v1.
        new Regex(@"^target\s+(?:opponent|player)\s+reveals\s+their\s+hand$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^you\s+choose\s+(?:a|an)\s+[\w\s,-]+\s+from\s+(?:it|that\s+player'?s?\s+hand)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^that\s+player\s+discards\s+that\s+card$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Anaphoric riders on direct-damage spells.
        new Regex(@"^its\s+controller\s+loses\s+\d+\s+life$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),

        // Anaphoric pump / keyword grants are NO LONGER noops — see
        // AnaphoricPump / AnaphoricKeyword handling below in TryExtractParams.
        // The regexes are matched before single-template binding so the
        // rider becomes a real pump/keyword grant on every creature target
        // the composed spell pulled in.

        // Choice / preamble clauses that bind nothing on their own.
        new Regex(@"^choose\s+(?:a\s+)?creature\s+type$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^choose\s+target\s+creature$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^choose\s+a\s+color$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // Late-bound to break the chicken-and-egg with the registry's
    // construction. Setter called from OracleSpellBinder after the
    // registry is built.
    private SpellTemplateRegistry? _registry;

    public void SetRegistry(SpellTemplateRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public int Priority => 200;
    public string Name => "ClauseComposition";

    public bool CanBind(SpellBindContext ctx) => _registry is not null;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    // Pure-parse path used by both live-binding (via DefaultTryBind) and
    // pre-compilation (offline). Walks every other template's
    // TryExtractParams against each clause; if every non-noop clause has
    // a winner, returns a dict with a single "clauses" key holding the
    // JSON-encoded sequence.
    // Anaphoric pump rider — "Each of them gets +1/+1 until end of turn".
    // Captures p/t (signed). Composer emits a synthetic __AnaphoricPump sub
    // that walks every chosen target in the composed spell and pumps each
    // Creature on resolve.
    private static readonly Regex AnaphoricPump = new(
        @"^(?:each\s+of\s+them|those\s+creatures|those\s+permanents|they|it)\s+gets?\s+(?<p>[+\-]\d+)/(?<t>[+\-]\d+)(?:\s+and\s+gains?\s+[\w\s,'-]+?)?\s+until\s+end\s+of\s+turn$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Anaphoric keyword grant — "Those creatures gain haste until end of turn".
    // Captures the keyword phrase. v1 grants the FIRST recognized keyword in
    // the phrase to each chosen target; multi-keyword chains ("haste and
    // trample") apply only the first.
    private static readonly Regex AnaphoricKeyword = new(
        @"^(?:each\s+of\s+them|those\s+creatures|those\s+permanents|they|it)\s+gain[s]?\s+(?<kw>[\w\s,'-]+?)\s+until\s+end\s+of\s+turn$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        if (_registry is null) return null;
        if (string.IsNullOrWhiteSpace(oracleText)) return null;

        var cleaned = ReminderText.Replace(oracleText, " ");
        cleaned = cleaned.Replace("\n", " ");
        cleaned = Whitespace.Replace(cleaned, " ").Trim();

        var clauses = SplitClauses(cleaned);
        if (clauses.Count < 2) return null;

        var encoded = new List<EncodedClause>();
        foreach (var raw in clauses)
        {
            var c = raw.Trim();
            if (c.Length == 0) continue;
            // Card-name-aware noop detection happens in TryBind via ctx;
            // at pure-parse time we don't have the card name. Treat
            // "Exile <CardName>" as a non-bindable noop here means it
            // would fail the all-bind check — handle it in TryBind only.
            // For pre-compile, just use the textual noop patterns.
            if (IsTextualNoop(c)) continue;

            // Anaphoric rider detection — synthetic sub-clauses that
            // reference the previous clause's target list rather than
            // binding their own template.
            var pumpMatch = AnaphoricPump.Match(c);
            if (pumpMatch.Success)
            {
                encoded.Add(new EncodedClause
                {
                    t = "__AnaphoricPump",
                    p = new Dictionary<string, string>
                    {
                        ["p"] = pumpMatch.Groups["p"].Value,
                        ["t"] = pumpMatch.Groups["t"].Value,
                    },
                });
                continue;
            }
            var kwMatch = AnaphoricKeyword.Match(c);
            if (kwMatch.Success)
            {
                encoded.Add(new EncodedClause
                {
                    t = "__AnaphoricKeyword",
                    p = new Dictionary<string, string> { ["kw"] = kwMatch.Groups["kw"].Value },
                });
                continue;
            }

            var winner = FindWinningTemplate(c);
            if (winner is null) return null;
            encoded.Add(winner);
        }

        if (encoded.Count < 2) return null;

        var json = JsonSerializer.Serialize(encoded);
        return new Dictionary<string, string> { ["clauses"] = json };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        if (_registry is null)
            throw new InvalidOperationException(
                "ClauseComposition.Rehydrate called before SetRegistry.");
        if (!@params.TryGetValue("clauses", out var json) || string.IsNullOrEmpty(json))
            throw new InvalidOperationException(
                "ClauseComposition params missing 'clauses' key.");

        var encoded = JsonSerializer.Deserialize<List<EncodedClause>>(json)
            ?? throw new InvalidOperationException(
                "ClauseComposition: failed to deserialize 'clauses' payload.");

        // Distinguish real sub-template clauses from synthetic anaphoric
        // riders. Real clauses get a SpellDefinition; anaphoric clauses get
        // a "tag" we propagate to Compose so it can route the full target
        // list (not a sliced one) at resolution time.
        var subs = new List<ComposedSub>(encoded.Count);
        foreach (var ec in encoded)
        {
            if (string.Equals(ec.t, "__AnaphoricPump", StringComparison.Ordinal))
            {
                var pp = int.Parse(ec.p?["p"] ?? "0");
                var tt = int.Parse(ec.p?["t"] ?? "0");
                subs.Add(ComposedSub.AnaphoricPump(pp, tt, ctx.Effects));
                continue;
            }
            if (string.Equals(ec.t, "__AnaphoricKeyword", StringComparison.Ordinal))
            {
                var kw = ec.p?["kw"] ?? string.Empty;
                subs.Add(ComposedSub.AnaphoricKeyword(kw, ctx.Effects));
                continue;
            }

            var template = _registry.OrderedTemplates
                .FirstOrDefault(t => string.Equals(t.Name, ec.t, StringComparison.Ordinal));
            if (template is null)
            {
                // Compiled DB references a template this build doesn't
                // know. Fall back to a no-op effect for this clause so
                // the rest of the composed spell still resolves.
                continue;
            }
            IReadOnlyDictionary<string, string> subParams = ec.p ?? new Dictionary<string, string>();
            if (!template.CanBind(ctx)) continue;
            subs.Add(ComposedSub.OfDefinition(
                template.Rehydrate(subParams, ctx).WithIntentStamp(template.Intent)));
        }

        return Compose(subs);
    }

    // Live-binding path falls through DefaultTryBind, which routes
    // through TryExtractParams + Rehydrate. The TryExtractParams path
    // uses textual noop only; card-name-aware noop ("Exile <self>") is
    // applied here as a second-stage filter before sub-binding.
    // (Implemented inside TryExtractParams' clause loop above for
    // pre-compile parity; the card-name version below is a defensive
    // hook for callers that want extra noop dropping at live-bind time.)

    private EncodedClause? FindWinningTemplate(string clause)
        // Returns null if no sub-template's TryExtractParams matches.

    {
        foreach (var t in _registry!.OrderedTemplates)
        {
            if (ReferenceEquals(t, this)) continue;
            var p = t.TryExtractParams(clause + ".");
            if (p is null) continue;
            return new EncodedClause
            {
                t = t.Name,
                p = p.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
        }
        return null;
    }

    private static List<string> SplitClauses(string text) =>
        text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static bool IsTextualNoop(string clause)
    {
        foreach (var rx in NoopClauses)
        {
            if (rx.IsMatch(clause)) return true;
        }
        return false;
    }

    private static SpellDefinition Compose(IReadOnlyList<ComposedSub> subs)
    {
        var allReqs = new List<TargetRequest>();
        var offsets = new int[subs.Count];
        for (var i = 0; i < subs.Count; i++)
        {
            offsets[i] = allReqs.Count;
            if (subs[i].Definition is { } d)
                allReqs.AddRange(d.TargetRequests);
        }
        var hasX = subs.Any(s => s.Definition?.HasVariableX == true);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: hasX,
            TargetRequests: allReqs,
            EffectFactory: p =>
            {
                var effects = new List<Abilities.IEffect>();
                for (var i = 0; i < subs.Count; i++)
                {
                    var sub = subs[i];
                    if (sub.Definition is { } d)
                    {
                        var start = offsets[i];
                        var count = d.TargetRequests.Count;
                        IReadOnlyList<IReadOnlyList<object>> slice;
                        if (count == 0)
                        {
                            slice = Array.Empty<IReadOnlyList<object>>();
                        }
                        else
                        {
                            var arr = new IReadOnlyList<object>[count];
                            for (var k = 0; k < count; k++)
                            {
                                arr[k] = (start + k) < p.Targets.Count
                                    ? p.Targets[start + k]
                                    : Array.Empty<object>();
                            }
                            slice = arr;
                        }
                        var subParams = new ChosenSpellParams(
                            ModeIndex: null,
                            X: p.X,
                            Targets: slice,
                            Mana: p.Mana,
                            AllPlayers: p.AllPlayers);
                        effects.AddRange(d.EffectFactory(subParams));
                    }
                    else if (sub.AnaphoricEffect is { } anaphoric)
                    {
                        // Anaphoric sub — receives the FULL composed target
                        // list, not a slice. The effect walks it for any
                        // Creature targets and applies the pump/keyword.
                        effects.Add(anaphoric(p));
                    }
                }
                return effects;
            });
    }

    private readonly record struct ComposedSub(
        SpellDefinition? Definition,
        Func<ChosenSpellParams, Abilities.IEffect>? AnaphoricEffect)
    {
        public static ComposedSub OfDefinition(SpellDefinition d) => new(d, null);

        public static ComposedSub AnaphoricPump(int p, int t, Majik.Core.Effects.ContinuousEffectsService? effects) =>
            new(null, _params => new Abilities.Effect(
                $"anaphoric pump {p:+#;-#;0}/{t:+#;-#;0} EOT",
                () =>
                {
                    // No effects service → silently skip (lossy v1 fallback).
                    if (effects == null) return;
                    foreach (var slot in _params.Targets)
                    {
                        foreach (var obj in slot)
                        {
                            if (obj is Cards.Creature c)
                            {
                                effects.Register(new Majik.Core.Effects.PumpUntilEndOfTurnEffect(c, p, t));
                            }
                        }
                    }
                }));

        public static ComposedSub AnaphoricKeyword(string kwPhrase, Majik.Core.Effects.ContinuousEffectsService? effects) =>
            new(null, _params => new Abilities.Effect(
                $"anaphoric grant {kwPhrase} EOT",
                () =>
                {
                    if (effects == null) return;
                    var kw = ExtractFirstKeyword(kwPhrase);
                    if (string.IsNullOrEmpty(kw)) return;
                    foreach (var slot in _params.Targets)
                    {
                        foreach (var obj in slot)
                        {
                            if (obj is Cards.Creature c)
                            {
                                effects.Register(new Majik.Core.Effects.GrantKeywordUntilEndOfTurnEffect(c, kw));
                            }
                        }
                    }
                }));
    }

    // Known evergreen keywords; first match in the phrase wins. Multi-keyword
    // chains ("haste and trample") apply only the first at v1.
    private static readonly string[] _knownKeywords = new[]
    {
        "flying", "first strike", "double strike", "deathtouch", "lifelink",
        "trample", "haste", "vigilance", "reach", "menace", "indestructible",
        "hexproof", "flash", "defender", "protection",
    };

    private static string ExtractFirstKeyword(string phrase)
    {
        var lower = phrase.ToLowerInvariant();
        foreach (var kw in _knownKeywords)
        {
            if (lower.Contains(kw, StringComparison.Ordinal)) return kw;
        }
        return string.Empty;
    }

    // System.Text.Json shape — short property names keep the
    // CompiledSpellTemplates.ParamsJson column compact.
    private sealed class EncodedClause
    {
        public string t { get; set; } = "";
        public Dictionary<string, string>? p { get; set; }
    }
}
