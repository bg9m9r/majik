using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.Game;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Multi-clause spell composer (last-resort, Priority = -1000). When no
/// single-template regex matches the whole oracle text, split it into
/// clauses, try to bind each clause independently against the rest of
/// the registry, and compose a single <see cref="SpellDefinition"/>
/// from the per-clause results.
///
/// All-or-nothing: every non-noop clause must bind, else this template
/// returns null and the card falls through to its vanilla shell.
///
/// Noop clauses (riders that v1 doesn't model — "It can't be
/// regenerated", "This damage can't be prevented", "Exile [self]") are
/// dropped before the all-bind check. Reminder text in parentheses is
/// stripped globally before splitting.
///
/// The composer concatenates each sub-spell's <see cref="TargetRequest"/>s
/// in order. At resolution time it slices the caster-provided
/// <see cref="ChosenSpellParams.Targets"/> per sub-spell so each
/// sub-EffectFactory sees only the targets in its own range — the
/// composed spell behaves as N sequential sub-spells.
/// </summary>
public sealed class ClauseCompositionTemplate : ISpellTemplate
{
    private static readonly Regex ReminderText = new(@"\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex[] NoopClauses = new[]
    {
        new Regex(@"^(it|they|that\s+creature|those\s+creatures)\s+can'?t\s+be\s+regenerated$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^this\s+damage\s+can'?t\s+be\s+prevented",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^the\s+damage\s+can'?t\s+be\s+prevented",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // Late-bound to break the chicken-and-egg with the registry's own
    // construction. The composer instance is added to the registry's
    // template list; the registry is what the composer needs to iterate.
    // Setter is called once from OracleSpellBinder after Registry is built.
    private SpellTemplateRegistry? _registry;

    public void SetRegistry(SpellTemplateRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    // Run BEFORE single-template binds — that way a multi-clause card
    // like "Return target creature to its owner's hand. Draw a card." is
    // bound to BOTH effects via composition rather than collapsing to
    // whichever single-template's regex matched first (typically just
    // the bounce, dropping the cantrip). Single-clause cards still fall
    // through cleanly since the composer requires 2+ bound clauses.
    public int Priority => 200;
    public string Name => "ClauseComposition";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (_registry is null) return null;
        var text = ctx.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = ReminderText.Replace(text, " ");
        cleaned = cleaned.Replace("\n", " ");
        cleaned = Whitespace.Replace(cleaned, " ").Trim();

        var clauses = SplitClauses(cleaned);
        if (clauses.Count < 2) return null;

        var binds = new List<SpellDefinition>();
        foreach (var clause in clauses)
        {
            var c = clause.Trim();
            if (c.Length == 0) continue;
            if (IsNoop(c, ctx.Entity.Name)) continue;

            var subEntity = new CardEntity
            {
                Name = ctx.Entity.Name,
                OracleText = c + ".",
            };
            var subCtx = ctx with { Entity = subEntity };

            var sub = TryBindClauseExcludingSelf(subCtx);
            if (sub is null) return null;
            binds.Add(sub);
        }

        if (binds.Count < 2) return null;
        return Compose(binds);
    }

    public bool CanBind(SpellBindContext ctx) => true;

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) => null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        throw new NotSupportedException(
            "ClauseComposition does not opt into pre-compilation. " +
            "Its split-and-bind logic depends on the live registry.");

    private SpellDefinition? TryBindClauseExcludingSelf(SpellBindContext subCtx)
    {
        foreach (var t in _registry!.OrderedTemplates)
        {
            if (ReferenceEquals(t, this)) continue;
            if (t.TryBind(subCtx) is { } def) return def;
        }
        return null;
    }

    private static List<string> SplitClauses(string text)
    {
        // Split on period. Card oracle text uses "." as the clause terminator.
        // Trailing fragment without a period is kept (some Scryfall texts).
        return text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static bool IsNoop(string clause, string cardName)
    {
        foreach (var rx in NoopClauses)
        {
            if (rx.IsMatch(clause)) return true;
        }
        // "Exile <CardName>" — many spells self-exile after resolution.
        // Self-exile under v1 is a no-op for spell resolution semantics
        // (the card is still moving through the stack).
        if (!string.IsNullOrWhiteSpace(cardName) &&
            string.Equals(clause, $"Exile {cardName}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    private static SpellDefinition Compose(IReadOnlyList<SpellDefinition> subs)
    {
        // Concatenate TargetRequests and remember each sub-spell's slot
        // range so we can slice ChosenSpellParams.Targets at resolution.
        var allReqs = new List<TargetRequest>();
        var offsets = new int[subs.Count];
        for (var i = 0; i < subs.Count; i++)
        {
            offsets[i] = allReqs.Count;
            allReqs.AddRange(subs[i].TargetRequests);
        }
        var hasX = subs.Any(s => s.HasVariableX);

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
                    var start = offsets[i];
                    var count = sub.TargetRequests.Count;
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
                    effects.AddRange(sub.EffectFactory(subParams));
                }
                return effects;
            });
    }
}
