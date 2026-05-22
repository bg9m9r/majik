using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

public sealed class InvestigateNTimesTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"investigate\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+times",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "InvestigateNTimes";
    public BotIntent Intent => BotIntent.Token;

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
        TokensSpellFactory.InvestigateNTimesSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(@params["n"]));
}
