using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class SurveilSelfTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*surveil\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public int Priority => 50;
    public string Name => "SurveilSelf";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? LibrarySpellFactory.SurveilSelfSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(m.Groups["n"].Value))
            : null;
    }
}
