using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class EachOpponentMillsTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"each\s+opponent\s+mills\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "EachOpponentMills";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? LibrarySpellFactory.EachOpponentMillsSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(m.Groups["n"].Value))
            : null;
    }
}
