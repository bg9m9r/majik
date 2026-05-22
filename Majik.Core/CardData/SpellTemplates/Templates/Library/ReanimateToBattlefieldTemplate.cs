using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
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
    // Mirrors the broadening done on ReanimateFromGraveyardTemplate. Accepts
    // "up to N", "X target", "any number of target", plural "cards",
    // multi-token kinds (artifact or enchantment, creature or Vehicle,
    // permanent, non-Aura enchantment, etc), and trailing inline filters
    // (mana value clauses, "with different names", "with total power N or
    // less", etc). Runtime stub reanimates ONE chosen target — v1
    // simplification for multi-target wordings. Mana-value / power / name
    // restrictions are lossy at v1.
    // Accepts both wordings:
    //   "Return target [kind] card from [scope] graveyard to the battlefield…"
    //   "Put target [kind] card from a graveyard onto the battlefield…"
    // (Necromantic Summons, Rise from the Grave, Vat Emergence use "put…onto".)
    private static readonly Regex Pattern = new(
        @"(?:return|put)\s+(?:(?:up\s+to\s+|any\s+number\s+of\s+)?(?:one|two|three|four|five|six|seven|eight|nine|ten|x)\s+)?target\s+(?<kind>(?:[\w-]+(?:\s+(?:or|and)\s+[\w-]+)*)?)\s*cards?\s+(?:[\w\s,-]*?\s+)?from\s+(?:your|a|an\s+opponent'?s?)\s+graveyard\s+(?:to|onto)\s+the\s+battlefield",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ReanimateToBattlefield";
    public BotIntent Intent => BotIntent.Reanimate;

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
