using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class GainControlTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"gain\s+control\s+of\s+target\s+creature",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "GainControl";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (ctx.Effects == null) return null;
        return SpellTemplateBindHelper.DefaultTryBind(this, ctx);
    }

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        ControlSpellFactory.GainControlSpell(ctx.Resolver, ctx.Caster, ctx.Effects!);
}
