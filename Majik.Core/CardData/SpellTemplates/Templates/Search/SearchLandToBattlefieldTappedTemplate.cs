using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

public sealed class SearchLandToBattlefieldTappedTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<kind>basic\s+land|land)\s+card[^.]*put\s+(?:it|that\s+card)\s+onto\s+the\s+battlefield\s+tapped",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "SearchLandToBattlefieldTapped";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? SearchSpellFactory.SearchLandToBattlefieldSpell(ctx.Caster, m.Groups["kind"].Value, tapped: true)
            : null;
    }
}
