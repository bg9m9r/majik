using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

public sealed class DealsXDamageAnyTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"deals?\s+x\s+damage\s+to\s+any\s+target",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "DealsXDamageAny";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? DamageSpellFactory.DealsXAnyTargetSpell(ctx.Resolver)
            : null;
}
