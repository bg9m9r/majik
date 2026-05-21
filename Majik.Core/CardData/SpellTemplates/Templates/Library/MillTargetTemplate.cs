using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class MillTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+player\s+mills\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "MillTarget";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? LibrarySpellFactory.MillTargetSpell(SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Resolver)
            : null;
    }
}
