using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class PumpCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+creature\s+gets\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "PumpCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? CountersSpellFactory.PumpSpell(
                int.Parse(m.Groups["p"].Value),
                int.Parse(m.Groups["t"].Value),
                ctx.Resolver)
            : null;
    }
}
