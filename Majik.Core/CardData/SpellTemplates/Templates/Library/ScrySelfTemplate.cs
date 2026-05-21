using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class ScrySelfTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*scry\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public int Priority => 60;
    public string Name => "ScrySelf";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? LibrarySpellFactory.ScryNSpell(ctx.Caster, ctx.Text, SpellTemplateHelpers.WordToInt(m.Groups["n"].Value))
            : null;
    }
}
