using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

public sealed class EachPlayerDrawsTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"each\s+player\s+draws\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "EachPlayerDraws";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ResourceSpellFactory.EachPlayerDrawsSpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value))
            : null;
    }
}
