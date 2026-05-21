using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

public sealed class DiscardTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+player\s+discards?\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "Discard";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ResourceSpellFactory.DiscardNSpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Resolver)
            : null;
    }
}
