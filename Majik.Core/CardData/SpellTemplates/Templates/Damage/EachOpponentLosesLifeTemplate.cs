using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

public sealed class EachOpponentLosesLifeTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"each\s+opponent\s+loses\s+(?<n>\d+|one|two|three|four|five|six|seven)\s+life",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "EachOpponentLosesLife";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? DamageSpellFactory.EachOpponentLosesLifeSpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Caster)
            : null;
    }
}
