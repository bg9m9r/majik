using System.Text.RegularExpressions;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

public sealed class CounterUnlessPayTemplate : ISpellTemplate
{
    // Optional "(non)creature" qualifier between "target" and "spell" — covers
    // Spell Pierce ("counter target noncreature spell unless ...") and the
    // hypothetical creature variant. Captured into the params dict so
    // Rehydrate can route to the right typed counter.
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+(?<qualifier>noncreature\s+|creature\s+)?spell\s+unless\s+its\s+controller\s+pays\s+\{?(?<n>\d+)\}?",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "CounterUnlessPay";
    public BotIntent Intent => BotIntent.Counter;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        // Capture the cost payable and the (optional) "(non)creature" type
        // qualifier so the compiled row stays faithful to the oracle and
        // Rehydrate can dispatch to the right factory variant.
        var qualifier = m.Groups["qualifier"].Success
            ? m.Groups["qualifier"].Value.Trim().ToLowerInvariant()
            : string.Empty;
        return new Dictionary<string, string>
        {
            ["n"] = m.Groups["n"].Value,
            ["q"] = qualifier,
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = int.TryParse(@params.TryGetValue("n", out var nv) ? nv : "0", out var parsed) ? parsed : 0;
        @params.TryGetValue("q", out var qualifier);
        var requireCreature = string.Equals(qualifier, "creature", StringComparison.OrdinalIgnoreCase);
        var requireNonCreature = string.Equals(qualifier, "noncreature", StringComparison.OrdinalIgnoreCase);
        return CounterSpellFactory.CounterTargetSpellUnlessPay(
            ctx.Resolver, ctx.Stack, n, requireCreature, requireNonCreature);
    }
}
