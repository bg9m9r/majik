using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

public sealed class YouGainLifeTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"you\s+gain\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "YouGainLife";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ResourceSpellFactory.YouGainLifeSpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Caster)
            : null;
    }
}
