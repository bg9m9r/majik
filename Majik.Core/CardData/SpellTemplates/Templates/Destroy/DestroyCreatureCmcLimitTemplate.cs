using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyCreatureCmcLimitTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+(?:nonland\s+)?creature\s+if\s+its\s+mana\s+value\s+is\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+or\s+less",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "DestroyCreatureCmcLimit";

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
        DestroySpellFactory.DestroyCreatureCmcLimitSpell(
            ctx.Resolver, SpellTemplateHelpers.WordToInt(@params["n"]));
}
