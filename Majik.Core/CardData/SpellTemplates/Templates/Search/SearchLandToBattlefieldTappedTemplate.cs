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

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kind"] = m.Groups["kind"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        SearchSpellFactory.SearchLandToBattlefieldSpell(ctx.Caster, @params["kind"], tapped: true);
}
