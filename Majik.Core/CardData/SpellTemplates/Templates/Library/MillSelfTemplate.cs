using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class MillSelfTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*mill\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public int Priority => 50;
    public string Name => "MillSelf";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? LibrarySpellFactory.MillSelfSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(m.Groups["n"].Value))
            : null;
    }
}
