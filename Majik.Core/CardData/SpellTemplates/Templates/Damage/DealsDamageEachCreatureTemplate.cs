using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

public sealed class DealsDamageEachCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+each\s+creature",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "DealsDamageEachCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? DamageSpellFactory.DealsDamageEachCreatureSpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Caster)
            : null;
    }
}
