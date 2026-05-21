using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

public sealed class InvestigateNTimesTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"investigate\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+times",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "InvestigateNTimes";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? TokensSpellFactory.InvestigateNTimesSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(m.Groups["n"].Value))
            : null;
    }
}
