using System.Text.RegularExpressions;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Counter;

public sealed class CounterCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"counter\s+target\s+creature\s+spell", RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CounterCreature";
    public BotIntent Intent => BotIntent.Counter;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CounterSpellFactory.CounterTypedSpell(ctx.Resolver, ctx.Stack, requireCreature: true);
}
