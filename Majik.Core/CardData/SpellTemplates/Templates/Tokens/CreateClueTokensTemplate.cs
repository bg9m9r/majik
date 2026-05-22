using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

public sealed class CreateClueTokensTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"create\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+clue\s+tokens?\b",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CreateClueTokens";
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
        TokensSpellFactory.CreateClueTokensSpell(ctx.Caster, SpellTemplateHelpers.WordToInt(@params["n"]));
}
