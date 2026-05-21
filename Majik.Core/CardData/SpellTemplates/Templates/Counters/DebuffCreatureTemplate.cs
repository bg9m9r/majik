using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

/// <summary>
/// "Target creature gets -N/-N until end of turn." — temporary stat
/// reduction (Weakness, Disfigure, Weigh Down, Murderous Cut style
/// removal). Distinct from <see cref="PutMinusCounterTemplate"/> (real
/// -1/-1 counters that persist) and from
/// <see cref="PumpCreatureTemplate"/> (positive stat boost).
///
/// Routes to <see cref="CountersSpellFactory.PumpSpell"/> with negated
/// values — the underlying <c>PumpUntilEndOfTurnEffect</c> handles
/// either sign cleanly.
/// </summary>
public sealed class DebuffCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"target\s+creature\s+gets\s+-(?<p>\d+)/-(?<t>\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "DebuffCreature";

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
            -int.Parse(@params["p"]),
            -int.Parse(@params["t"]),
            ctx.Resolver);
}
