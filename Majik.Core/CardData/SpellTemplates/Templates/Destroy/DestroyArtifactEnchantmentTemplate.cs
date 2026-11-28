using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyArtifactEnchantmentTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+(artifact|enchantment)(\s+or\s+(artifact|enchantment))?",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "DestroyArtifactEnchantment";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    // CR 115.1 — the printed type words decide what can be targeted:
    // "target artifact" is not "target artifact or enchantment". The pattern
    // accepts all three forms; before, every form bound the same
    // "artifact or enchantment" request (Shatter, Kolaghan's Command,
    // Rain of Thorns, Destroy Evil, ...).
    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        if (!m.Success) return null;
        var words = new[] { m.Groups[1].Value, m.Groups[3].Value }
            .Where(w => w.Length > 0).Select(w => w.ToLowerInvariant()).ToHashSet();
        return new Dictionary<string, string>
        {
            ["artifact"] = words.Contains("artifact") ? "1" : "0",
            ["enchantment"] = words.Contains("enchantment") ? "1" : "0",
        };
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        // Older callers without the params keep the two-type form.
        var artifact = !@params.TryGetValue("artifact", out var a) || a == "1";
        var enchantment = !@params.TryGetValue("enchantment", out var e) || e == "1";
        return DestroySpellFactory.DestroyArtifactOrEnchantmentSpell(ctx.Resolver, artifact, enchantment);
    }
}
