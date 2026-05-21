using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class BounceTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"return\s+target\s+(?<kind>permanent|creature|artifact|enchantment|nonland\s+permanent|land)\s+to\s+(its|their)\s+owner'?s?\s+hand",
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
