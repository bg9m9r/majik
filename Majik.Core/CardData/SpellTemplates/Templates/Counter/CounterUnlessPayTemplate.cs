using System.Text.RegularExpressions;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

public sealed class CounterUnlessPayTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+spell\s+unless\s+its\s+controller\s+pays\s+\{?(?<n>\d+)\}?",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "CounterUnlessPay";
    public BotIntent Intent => BotIntent.Counter;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        // Capture the cost payable even though we don't simulate the "unless
        // pay" rider yet — recording it now keeps the compiled row faithful
        // to the oracle and lets a future Rehydrate consume it.
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CounterSpellFactory.CounterTargetSpell(ctx.Resolver, ctx.Stack);
}
