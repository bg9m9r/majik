using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Damage;

/// <summary>
/// "Deals N damage to target creature." — creature-only damage
/// (Lightning Strike, Char, Magma Spray, hundreds of others).
/// Distinct from <see cref="DamageAnyTargetTemplate"/>: that one
/// matches "any target" (any creature / player / planeswalker);
/// this one matches "target creature" only.
///
/// Priority 60 — must beat <see cref="DamageAnyTargetTemplate"/>
/// (priority 50) so "deals 3 damage to target creature" isn't
/// over-broadened to "any target" by accident. The
/// <c>DamageAnyTarget</c> regex requires the literal word "any"
/// before "target", so an ordering mismatch wouldn't actually
/// collide today — but bumping the priority makes the intent
/// declarative.
/// </summary>
public sealed class DamageCreatureTemplate : ISpellTemplate
{
    // Accepts an optional modifier chain between "target" and "creature":
    // combat-state ("attacking", "blocking", "attacking or blocking",
    // "tapped"), color ("nonwhite", "nonred", "white or blue", "Treefolk"
    // tribe, "non-Elemental"), etc. The runtime stub deals N damage to
    // whatever creature is chosen — modifier informs target legality, not
    // the resolved effect, which keeps the stub faithful.
    private static readonly Regex Pattern = new(
        @"deals?\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+damage\s+to\s+target\s+(?:(?:[\w-]+|or)\s*,?\s*){0,5}creature\b",
        RegexOptions.IgnoreCase);

    public int Priority => 60;
    public string Name => "DamageTargetCreature";

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
        DamageSpellFactory.DamageCreatureSpell(
            SpellTemplateHelpers.WordToInt(@params["n"]), ctx.Resolver);
}
