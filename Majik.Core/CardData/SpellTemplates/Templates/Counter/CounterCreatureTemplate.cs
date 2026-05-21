using System.Text.RegularExpressions;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

public sealed class CounterCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+creature\s+spell", RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CounterCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? CounterSpellFactory.CounterTypedSpell(ctx.Resolver, ctx.Stack, requireCreature: true)
            : null;
}
