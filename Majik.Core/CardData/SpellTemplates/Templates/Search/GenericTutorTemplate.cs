using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

/// <summary>
/// "Search your library for a card, ... put that card into your hand,
/// then shuffle." — generic tutor with no type restriction (Demonic
/// Tutor, Vampiric Tutor, Cruel Tutor, etc).
///
/// Distinct from <see cref="SearchLibraryTemplate"/> which requires a
/// specific kind (creature / artifact / enchantment / …). This template
/// matches the more permissive "a card" form. Priority 10 so any
/// kind-specific tutor wins first; only cards that mention "a card"
/// without a type rider end up here.
/// </summary>
public sealed class GenericTutorTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"search\s+your\s+library\s+for\s+a\s+card[^.]*put\s+(?:it|that\s+card)\s+into\s+your\s+hand",
        RegexOptions.IgnoreCase);

    public int Priority => 10;
    public string Name => "GenericTutor";
    public BotIntent Intent => BotIntent.Tutor;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        SearchSpellFactory.SearchLibrarySpell(ctx.Caster, "card");
}
