using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

public sealed class TargetPlayerLosesLifeTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+(?:player|opponent)\s+loses?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+life",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "TargetPlayerLosesLife";

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
        ResourceSpellFactory.TargetPlayerLosesLifeSpell(
            SpellTemplateHelpers.WordToInt(@params["n"]), ctx.Resolver);
}
