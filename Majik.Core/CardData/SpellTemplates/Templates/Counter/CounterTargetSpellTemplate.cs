using System.Text.RegularExpressions;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

public sealed class CounterTargetSpellTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+spell", RegexOptions.IgnoreCase);

    public int Priority => 10;
    public string Name => "CounterTargetSpell";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? CounterSpellFactory.CounterTargetSpell(ctx.Resolver, ctx.Stack)
            : null;
}
