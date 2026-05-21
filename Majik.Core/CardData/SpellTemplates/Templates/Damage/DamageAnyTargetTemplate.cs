using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

public sealed class DamageAnyTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+any\s+target",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "DamageAnyTarget";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? DamageSpellFactory.DamageAnySpell(
                SpellTemplateHelpers.WordToInt(m.Groups["n"].Value), ctx.Resolver)
            : null;
    }
}
