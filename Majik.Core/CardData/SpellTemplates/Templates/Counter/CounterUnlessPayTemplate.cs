using System.Text.RegularExpressions;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

public sealed class CounterUnlessPayTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+spell\s+unless\s+its\s+controller\s+pays\s+\{?(?<n>\d+)\}?",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "CounterUnlessPay";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? CounterSpellFactory.CounterTargetSpell(ctx.Resolver, ctx.Stack)
            : null;
}
