using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Counters;

/// <summary>
/// "All creatures get [+|-]N/[+|-]M until end of turn." — symmetrical
/// pump or debuff sweep (Infest, Nausea, Pestilent Haze, Golden Demise,
/// Mutilate, Toxic Deluge — debuff side; Overrun, Spear of Heliod's
/// trigger... — pump side, though most pump-all-creatures clauses live
/// on permanents).
///
/// Sign-agnostic: both +N/+M and -N/-M (and mixed +/-) bind here. The
/// v1 stub registers a PumpUntilEndOfTurnEffect per creature on the
/// caster's view of the battlefield — opponents' creatures are out of
/// reach until SpellCastFlow exposes AllPlayers.
///
/// Priority 80 so it wins against the targeted PumpCreature /
/// DebuffCreature templates (priority 50) for the "all creatures" form.
/// </summary>
public sealed class AllCreaturesPumpTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"all\s+creatures\s+get\s+(?<p>[+-]\d+)/(?<t>[+-]\d+)\s+until\s+end\s+of\s+turn",
        RegexOptions.IgnoreCase);

    public int Priority => 80;
    public string Name => "AllCreaturesPump";

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
        CountersSpellFactory.AllCreaturesPumpSpell(
            int.Parse(@params["p"]),
            int.Parse(@params["t"]),
            ctx.Caster);
}
