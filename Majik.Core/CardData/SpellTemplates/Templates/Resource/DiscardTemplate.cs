using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Resource;

public sealed class DiscardTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+player\s+discards?\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+cards?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "Discard";

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
        ResourceSpellFactory.DiscardNSpell(
            SpellTemplateHelpers.WordToInt(@params["n"]), ctx.Resolver);
}
