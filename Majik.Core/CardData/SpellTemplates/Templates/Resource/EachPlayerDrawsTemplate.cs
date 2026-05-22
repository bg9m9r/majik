using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

public sealed class EachPlayerDrawsTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"each\s+player\s+draws\s+(?<n>\d+|a|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "EachPlayerDraws";
    public BotIntent Intent => BotIntent.Draw;

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
        ResourceSpellFactory.EachPlayerDrawsSpell(
            SpellTemplateHelpers.WordToInt(@params["n"]));
}
