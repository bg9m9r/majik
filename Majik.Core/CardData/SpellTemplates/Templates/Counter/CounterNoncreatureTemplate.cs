using System.Text.RegularExpressions;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

public sealed class CounterNoncreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+noncreature\s+spell", RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CounterNoncreature";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? CounterSpellFactory.CounterTypedSpell(ctx.Resolver, ctx.Stack, requireNonCreature: true)
            : null;
}
