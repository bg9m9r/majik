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

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? TokensSpellFactory.CreateTokensSpell(
                ctx.Caster,
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value),
                int.Parse(m.Groups["p"].Value),
                int.Parse(m.Groups["t"].Value),
                m.Groups["subtype"].Value,
                TokensSpellFactory.ParseKeywordList(m.Groups["keywords"].Value))
            : null;
    }
}
