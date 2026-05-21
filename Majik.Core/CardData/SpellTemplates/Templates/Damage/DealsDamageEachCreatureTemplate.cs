using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

public sealed class DealsDamageEachCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+each\s+creature",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "DealsDamageEachCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DamageSpellFactory.DealsDamageEachCreatureSpell(
            SpellTemplateHelpers.WordToInt(@params["n"]), ctx.Caster);
}
