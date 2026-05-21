using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

public sealed class SearchLibraryTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"search\s+your\s+library\s+for\s+a\s+(?<kind>basic\s+land|land|creature|artifact|enchantment|instant|sorcery|planeswalker)\s+card",
        RegexOptions.IgnoreCase);

    public int Priority => 10;
    public string Name => "SearchLibrary";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? SearchSpellFactory.SearchLibrarySpell(ctx.Caster, m.Groups["kind"].Value)
            : null;
    }
}
