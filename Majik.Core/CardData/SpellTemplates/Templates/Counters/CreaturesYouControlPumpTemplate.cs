using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

public sealed class CreaturesYouControlPumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"creatures\s+you\s+control\s+get\s+\+(?<p>\d+)/\+(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "CreaturesYouControlPump";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public bool CanBind(SpellBindContext ctx) => ctx.Effects != null;

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
        CountersSpellFactory.CreaturesYouControlPumpSpell(
            int.Parse(@params["p"]),
            int.Parse(@params["t"]),
            ctx.Caster, ctx.Effects!);
}
