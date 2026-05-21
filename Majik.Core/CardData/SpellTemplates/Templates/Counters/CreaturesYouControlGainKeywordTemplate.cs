using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class CreaturesYouControlGainKeywordTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"creatures\s+you\s+control\s+gain\s+(?<kw>flying|trample|first\s+strike|double\s+strike|deathtouch|lifelink|vigilance|haste|reach|menace|indestructible)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CreaturesYouControlGainKeyword";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        if (ctx.Effects == null) return null;
        return SpellTemplateBindHelper.DefaultTryBind(this, ctx);
    }

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kw"] = m.Groups["kw"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        CountersSpellFactory.CreaturesYouControlGainKeywordSpell(
            CountersSpellFactory.NormaliseKeyword(@params["kw"]),
            ctx.Caster, ctx.Effects!);
}
