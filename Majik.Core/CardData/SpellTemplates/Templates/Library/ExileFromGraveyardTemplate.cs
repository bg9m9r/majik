using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class ExileFromGraveyardTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"exile\s+target\s+(?<kind>creature|instant|sorcery|artifact|enchantment|planeswalker|land)?\s*card\s+from\s+(?:a|your)\s+graveyard",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ExileFromGraveyard";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kind"] = m.Groups["kind"].Value.Trim() }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        LibrarySpellFactory.ExileFromGraveyardSpell(ctx.Resolver, @params["kind"]);
}
