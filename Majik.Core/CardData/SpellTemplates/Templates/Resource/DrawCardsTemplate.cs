using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

public sealed class DrawCardsTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"draw\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "DrawCards";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ResourceSpellFactory.DrawNSpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Caster)
            : null;
    }
}
