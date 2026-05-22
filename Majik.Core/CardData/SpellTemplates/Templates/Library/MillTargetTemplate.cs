using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class MillTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+(?:player|opponent)\s+mills\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "MillTarget";
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
        LibrarySpellFactory.MillTargetSpell(SpellTemplateHelpers.WordToInt(@params["n"]), ctx.Resolver);
}
