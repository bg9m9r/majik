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
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DamageSpellFactory.DealsXAnyTargetSpell(ctx.Resolver, ctx.Replacements, ctx.Caster);
}
