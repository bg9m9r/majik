using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

public sealed class YouLoseLifeTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*you\s+lose\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public int Priority => 50;
    public string Name => "YouLoseLife";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ResourceSpellFactory.YouLoseLifeSpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Caster)
            : null;
    }
}
