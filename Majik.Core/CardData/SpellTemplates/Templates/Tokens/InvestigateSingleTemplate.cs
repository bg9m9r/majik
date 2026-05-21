using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Tokens;

public sealed class InvestigateSingleTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^\s*investigate\s*\.",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public int Priority => 50;
    public string Name => "InvestigateSingle";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        return Pattern.IsMatch(ctx.Text)
            ? TokensSpellFactory.InvestigateNTimesSpell(ctx.Caster, 1)
            : null;
    }
}
