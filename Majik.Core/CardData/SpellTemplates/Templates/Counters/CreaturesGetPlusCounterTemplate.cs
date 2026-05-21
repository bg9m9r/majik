using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class CreaturesGetPlusCounterTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"each\s+creature\s+you\s+control\s+gets\s+a\s+\+1/\+1\s+counter\s+on\s+it",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CreaturesGetPlusCounter";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? CountersSpellFactory.CreaturesGetPlusCounterSpell(ctx.Caster)
            : null;
}
