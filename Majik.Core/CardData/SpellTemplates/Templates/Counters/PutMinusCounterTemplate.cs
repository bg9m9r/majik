using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class PutMinusCounterTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"put\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+-1/-1\s+counters?\s+on\s+target\s+creature",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "PutMinusCounter";

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
        CountersSpellFactory.PutMinusOneMinusOneSpell(
            SpellTemplateHelpers.WordToInt(@params["n"]), ctx.Resolver);
}
