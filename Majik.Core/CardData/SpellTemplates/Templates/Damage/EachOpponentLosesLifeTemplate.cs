using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

public sealed class EachOpponentLosesLifeTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"each\s+opponent\s+loses\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+life",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "EachOpponentLosesLife";
    public BotIntent Intent => BotIntent.Burn | BotIntent.Reach;

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
        DamageSpellFactory.EachOpponentLosesLifeSpell(
            SpellTemplateHelpers.WordToInt(@params["n"]), ctx.Caster);
}
