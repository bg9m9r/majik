using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

public sealed class DealsDamageEachCreatureTemplate : ISpellTemplate
{
    // Accepts an optional modifier chain between "each" and "creature":
    // "each nontoken creature" (Incandescent Aria), "each nonartifact
    // creature" (Whipflare), "each non-Dragon creature" (Breath Weapon),
    // "each attacking creature" (Rain of Blades), "each white and/or
    // blue creature" (Ember Gale-tail), "each creature your opponents
    // control" (Disaster Radius). Runtime stub damages every creature
    // on the caster's view of the battlefield — modifier informational
    // at v1.
    private static readonly Regex Pattern = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+each\s+(?:(?:[\w-]+|or|and|and/or)\s*,?\s*){0,4}creature(?:\s+(?:you\s+control|your\s+opponents\s+control|an\s+opponent\s+controls))?",
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
