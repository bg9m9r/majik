using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "[Source] deals X damage to target creature" — variable-X creature
/// burn (Heat Ray, Hobbit's Sting, Galvanic Bombardment, Welding Sparks,
/// Goblin Negotiation, etc). Mirrors <see cref="DealsXDamageAnyTemplate"/>
/// but constrains the target to creatures only.
///
/// Priority 100 so the numeric variant (DamageCreatureTemplate, priority
/// 50) cannot accidentally win — though it can't anyway since "X" isn't
/// in its alternation list.
/// </summary>
public sealed class DealsXDamageCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"deals?\s+x\s+damage\s+to\s+target\s+creature\b",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "DealsXDamageCreature";
    public BotIntent Intent => BotIntent.Burn;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DamageSpellFactory.DealsXCreatureSpell(ctx.Resolver, ctx.Replacements, ctx.Caster, ctx.EventBus);
}
