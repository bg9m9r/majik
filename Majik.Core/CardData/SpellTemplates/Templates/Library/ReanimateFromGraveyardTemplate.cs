using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class ReanimateFromGraveyardTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"return\s+target\s+(?<kind>card|creature|instant|sorcery|artifact|enchantment|planeswalker|land)?\s*card\s+from\s+your\s+graveyard\s+to\s+your\s+hand",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ReanimateFromGraveyard";

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
        LibrarySpellFactory.ReanimateSpell(ctx.Resolver, @params["kind"]);
}
