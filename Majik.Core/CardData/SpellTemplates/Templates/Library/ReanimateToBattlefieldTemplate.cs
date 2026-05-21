using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// "Return target [kind] card from your graveyard to the battlefield."
/// — true reanimation (Animate Dead, Reanimate, Zombify, etc).
/// Distinct from <see cref="ReanimateFromGraveyardTemplate"/>: that
/// returns to <em>hand</em> (Raise Dead style); this template returns
/// to <em>battlefield</em>.
///
/// v1 reanimation skips ETB triggers and just moves the card +
/// transfers control to the caster (CR 110.2). Full ETB-trigger
/// support requires routing through ZoneService — deferred.
/// </summary>
public sealed class ReanimateToBattlefieldTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"return\s+target\s+(?<kind>card|creature|instant|sorcery|artifact|enchantment|planeswalker|land)?\s*card\s+from\s+your\s+graveyard\s+to\s+the\s+battlefield",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ReanimateToBattlefield";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kind"] = m.Groups["kind"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        LibrarySpellFactory.ReanimateToBattlefieldSpell(
            ctx.Caster, ctx.Resolver, @params.TryGetValue("kind", out var k) ? k : "");
}
