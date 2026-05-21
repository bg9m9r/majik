using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class TapTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"\btap\s+target\s+(?<kind>permanent|creature|artifact|land|enchantment|planeswalker)",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "TapTarget";

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
        ControlSpellFactory.TapTargetSpell(ctx.Resolver, $"target {@params["kind"]}");
}
