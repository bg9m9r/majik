using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

public sealed class CreateClueTokensTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+clue\s+tokens?\b",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CreateClueTokens";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? TokensSpellFactory.CreateClueTokensSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(m.Groups["n"].Value))
            : null;
    }
}
