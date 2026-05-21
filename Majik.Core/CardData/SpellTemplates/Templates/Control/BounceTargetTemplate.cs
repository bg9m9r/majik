using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class BounceTargetTemplate : ISpellTemplate
{
    // Covers single-clause bounce stubs. The kind group is intentionally broad
    // so we pick up modifier-heavy phrasings (e.g. "creature an opponent controls",
    // "nonland permanent you don't control", "creature or Vehicle"). The runtime
    // stub ignores the modifier and returns the chosen target to its owner's hand,
    // which is a faithful v1 of "return target X to its owner's hand".
    private static readonly Regex Pattern = new(
        @"return\s+target\s+(?<kind>(?:nontoken\s+|nonland\s+|enchanted\s+)?(?:permanent|creature|artifact|enchantment|land|spell|vehicle|nonland\s+permanent|creature\s+or\s+enchantment|creature\s+or\s+vehicle|artifact\s+or\s+enchantment|artifact\s+creature|artifact,\s+enchantment,?\s+or\s+land|spell\s+or\s+creature|spell\s+or\s+nonland\s+permanent|nonland\s+permanent\s+or\s+suspended\s+card)(?:\s+(?:an\s+opponent\s+controls|you\s+control|you\s+don'?t\s+control))?)\s+to\s+(its|their)\s+owner'?s?\s+hand",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "BounceTarget";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kind"] = m.Groups["kind"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        ControlSpellFactory.BounceTargetSpell(ctx.Resolver, $"target {@params["kind"]}");
}
