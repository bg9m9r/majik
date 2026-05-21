using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class CreaturesYouControlPumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"creatures\s+you\s+control\s+get\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CreaturesYouControlPump";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (ctx.Effects == null) return null;
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? CountersSpellFactory.CreaturesYouControlPumpSpell(
                int.Parse(m.Groups["p"].Value),
                int.Parse(m.Groups["t"].Value),
                ctx.Caster, ctx.Effects)
            : null;
    }
}
