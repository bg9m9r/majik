using System.Text.RegularExpressions;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

/// <summary>
/// "Counter target spell. You gain N life." (Absorb — CR 701.5 + CR 119.3) —
/// the composite counter-then-lifegain spell template. Binds through the
/// declarative <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> path
/// as a TWO-verb composition (<see cref="CounterTargetSpellEffectDef"/> +
/// <see cref="GainLifeSelfEffectDef"/>), so BOTH printed clauses resolve in
/// order (CR 608.2c) instead of the generic single-clause
/// <see cref="CounterTargetSpellTemplate"/> binding only the counter and
/// silently dropping the lifegain rider.
///
/// <para>
/// Higher <see cref="Priority"/> than the plain counter template so the
/// composite text is preferred whenever the lifegain clause is present.
/// </para>
/// </summary>
public sealed class CounterAndGainLifeTemplate : ISpellTemplate
{
    // "counter target spell. you gain 3 life." — the lifegain amount is
    // captured (Absorb = 3); any "you gain N life" rider on a plain counter
    // routes through the same composition.
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+spell\s*\.\s*you\s+gain\s+(\d+)\s+life",
        RegexOptions.IgnoreCase);

    public int Priority => 20;
    public string Name => "CounterAndGainLife";
    public BotIntent Intent => BotIntent.Counter;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText ?? string.Empty);
        return m.Success
            ? new Dictionary<string, string> { ["life"] = m.Groups[1].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        ArgumentNullException.ThrowIfNull(@params);
        var life = @params.TryGetValue("life", out var raw) && int.TryParse(raw, out var n) ? n : 0;
        return CardDefRuntime.BuildSpellDefinitionFromEffects(
            ctx.Entity.Name,
            new EffectDefinition[]
            {
                new CounterTargetSpellEffectDef(),
                new GainLifeSelfEffectDef { Amount = life },
            });
    }
}
