using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class GainControlTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"gain\s+control\s+of\s+target\s+creature",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "GainControl";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (ctx.Effects == null) return null;
        return Pattern.IsMatch(ctx.Text)
            ? ControlSpellFactory.GainControlSpell(ctx.Resolver, ctx.Caster, ctx.Effects)
            : null;
    }
}
