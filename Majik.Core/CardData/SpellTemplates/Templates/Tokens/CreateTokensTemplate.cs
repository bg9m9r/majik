using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

public sealed class CreateTokensTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(?<p>\d+)/(?<t>\d+)\s+(?<colour>white|blue|black|red|green|colorless)?\s*(?<subtype>[A-Za-z]+)\s+creature\s+tokens?(?:\s+with\s+(?<keywords>[A-Za-z, ]+))?",
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

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        TokensSpellFactory.CreateTokensSpell(
            ctx.Caster,
            SpellTemplateHelpers.WordToInt(@params["n"]),
            int.Parse(@params["p"]),
            int.Parse(@params["t"]),
            @params["subtype"],
            TokensSpellFactory.ParseKeywordList(@params["keywords"]));
}
