using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

public sealed class SearchLandToBattlefieldTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<kind>basic\s+land|land)\s+card[^.]*put\s+(?:it|that\s+card)\s+onto\s+the\s+battlefield",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "SearchLandToBattlefield";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? SearchSpellFactory.SearchLandToBattlefieldSpell(ctx.Caster, m.Groups["kind"].Value, tapped: false)
            : null;
    }
}
