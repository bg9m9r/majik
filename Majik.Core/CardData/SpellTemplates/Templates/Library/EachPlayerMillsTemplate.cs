using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class EachPlayerMillsTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"each\s+player\s+mills\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "EachPlayerMills";
    public BotIntent Intent => BotIntent.Mill;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        LibrarySpellFactory.EachPlayerMillsSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(@params["n"]));
}
