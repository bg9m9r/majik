using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class BounceTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"return\s+target\s+(permanent|creature|artifact|enchantment|nonland\s+permanent|land)\s+to\s+(its|their)\s+owner'?s?\s+hand",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "BounceTarget";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ControlSpellFactory.BounceTargetSpell(ctx.Resolver, $"target {m.Groups[1].Value}")
            : null;
    }
}
