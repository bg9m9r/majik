using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

public sealed class CreateTokensTemplate : ISpellTemplate
{
    // Captures both fixed-count ("create two 2/2 ...") and variable-X
    // ("create X 1/1 ...") token-creation clauses, with optional "tapped"
    // prefix on P/T, multi-color qualifiers ("red and green", "white and
    // black"), multi-word subtypes ("Human Knight", "Elf Warrior",
    // "Phyrexian Golem"), and optional "artifact creature" / "creature"
    // qualifier. Trailing "with <keyword(s)>" is preserved.
    //
    // v1 stub: when N is "x" we resolve as 0 (no tokens; the spell still
    // binds and resolves without error). When P or T is "x" they similarly
    // collapse to 0 — tokens spawn as 0/0 which die to SBA, accurately
    // reflecting "no X paid" semantics. Multi-color qualifier is
    // informational only; subtype enum lookup falls back to no-subtype on
    // misses.
    private static readonly Regex Pattern = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten|x)\s+(?:tapped\s+)?(?<p>\d+|x)/(?<t>\d+|x)\s+(?<colour>(?:white|blue|black|red|green|colorless)(?:\s+(?:and|or)\s+(?:white|blue|black|red|green|colorless))*\s+)?(?<subtype>[A-Za-z][\w-]*(?:\s+[A-Za-z][\w-]*)?)\s+(?:artifact\s+)?creature\s+tokens?(?:\s+with\s+(?<keywords>[A-Za-z, ]+))?",
        RegexOptions.IgnoreCase);

    public int Priority => 10;
    public string Name => "CreateTokens";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string>
            {
                ["n"]        = m.Groups["n"].Value,
                ["p"]        = m.Groups["p"].Value,
                ["t"]        = m.Groups["t"].Value,
                ["colour"]   = m.Groups["colour"].Value,
                ["subtype"]  = m.Groups["subtype"].Value,
                ["keywords"] = m.Groups["keywords"].Value,
            }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        // "x" in count/P/T collapses to 0 — the spell still binds and resolves;
        // 0/0 tokens die to SBA, which is faithful to "X = 0" semantics when
        // no real X-resolution machinery is in place yet.
        var n = ParseIntOrZero(@params["n"]);
        var p = ParseIntOrZero(@params["p"]);
        var t = ParseIntOrZero(@params["t"]);
        return TokensSpellFactory.CreateTokensSpell(
            ctx.Caster, n, p, t,
            @params["subtype"],
            TokensSpellFactory.ParseKeywordList(@params["keywords"]));
    }

    private static int ParseIntOrZero(string s) =>
        string.Equals(s, "x", StringComparison.OrdinalIgnoreCase)
            ? 0
            : SpellTemplateHelpers.WordToInt(s);
}
