using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyPermanentTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+permanent",
        RegexOptions.IgnoreCase);

    public int Priority => 10;
    public string Name => "DestroyPermanent";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DestroySpellFactory.DestroyTargetSpell(
            ctx.Resolver, "target permanent", _ => true);
}
