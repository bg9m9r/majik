using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class GrantKeywordTilEotTemplate : ISpellTemplate
{
    // Accepts optional control qualifier after "creature" — "target creature
    // you control", "target creature an opponent controls", etc. Runtime stub
    // ignores the qualifier (target legality is enforced earlier in the cast
    // flow). Also accepts hexproof + shroud — keywords recognised as markers
    // even when their full semantics aren't wired.
    private static readonly Regex Pattern = new(
        @"target\s+creature(?:\s+(?:you\s+control|an\s+opponent\s+controls|you\s+don'?t\s+control))?\s+gains?\s+(?<kw>flying|trample|first\s+strike|double\s+strike|deathtouch|lifelink|vigilance|haste|reach|menace|indestructible|hexproof|shroud)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "GrantKeywordTilEot";
    public BotIntent Intent => BotIntent.CombatTrick | BotIntent.Buff;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kw"] = m.Groups["kw"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CountersSpellFactory.GrantKeywordSpell(
            CountersSpellFactory.NormaliseKeyword(@params["kw"]), ctx.Resolver);
}
