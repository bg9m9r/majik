using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class PutPlusCounterTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"put\s+(?<n>a|an|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+\+1/\+1\s+counters?\s+on\s+target\s+creature",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "PutPlusCounter";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? CountersSpellFactory.PutPlusOnePlusOneSpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Resolver)
            : null;
    }
}
