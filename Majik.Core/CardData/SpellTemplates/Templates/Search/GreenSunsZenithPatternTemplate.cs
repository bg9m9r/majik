using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

public sealed class GreenSunsZenithPatternTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<color>green|white|blue|black|red)\s+creature\s+card\s+with\s+mana\s+value\s+x\s+or\s+less[^.]*put\s+it\s+onto\s+the\s+battlefield[^.]*shuffle\.\s*shuffle[^.]+into\s+its\s+owner'?s?\s+library",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "GreenSunsZenithPattern";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["color"] = m.Groups["color"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        SearchSpellFactory.GreenSunsZenithSpell(ctx.Caster, @params["color"]);
}
