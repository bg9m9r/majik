using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class GrantKeywordTilEotTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+creature\s+gains?\s+(?<kw>flying|trample|first\s+strike|double\s+strike|deathtouch|lifelink|vigilance|haste|reach|menace|indestructible)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "GrantKeywordTilEot";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? CountersSpellFactory.GrantKeywordSpell(
                CountersSpellFactory.NormaliseKeyword(m.Groups["kw"].Value), ctx.Resolver)
            : null;
    }
}
