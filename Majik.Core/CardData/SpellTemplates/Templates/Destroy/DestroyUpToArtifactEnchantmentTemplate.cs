using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyUpToArtifactEnchantmentTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+up\s+to\s+(?<n>\d+|one|two|three|four|five)\s+target\s+artifacts?\s+and(?:/or)?\s+enchantments?",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "DestroyUpToArtifactEnchantment";

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
        DestroySpellFactory.DestroyUpToArtifactEnchantmentSpell(
            ctx.Resolver, SpellTemplateHelpers.WordToInt(@params["n"]));
}
