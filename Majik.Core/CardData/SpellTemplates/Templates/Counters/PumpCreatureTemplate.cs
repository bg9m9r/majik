using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class PumpCreatureTemplate : ISpellTemplate
{
    // Captures "+P/+T [and gains <keyword(s)>] until end of turn". Trailing
    // "and gains <X>" is dropped at runtime — v1 stub only applies the
    // numeric pump. Granting a keyword until eot is a separate concern
    // (future: compose with a GrantKeywordTilEot effect in the resolved spell).
    // Optional control modifier between "creature" and "gets"
    // ("target creature you control gets +N/+N..." — Shore Up,
    // Ranger's Guile; rarer "you don't control" / "an opponent controls"
    // variants). v1 stub applies the pump regardless of control
    // predicate; the modifier is informational at this layer (target
    // legality lives in the predicate).
    private static readonly Regex Pattern = new(
        @"target\s+creature(?:\s+(?:you\s+control|an\s+opponent\s+controls|you\s+don'?t\s+control))?\s+gets\s+\+(?<p>\d+)/\+(?<t>\d+)(?:\s+and\s+gains?\s+[\w\s,-]+?)?\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "PumpCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string>
            {
                ["p"] = m.Groups["p"].Value,
                ["t"] = m.Groups["t"].Value,
            }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CountersSpellFactory.PumpSpell(
            int.Parse(@params["p"]),
            int.Parse(@params["t"]),
            ctx.Resolver);
}
