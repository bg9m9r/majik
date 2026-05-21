using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class ExileTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"exile\s+target\s+(creature|permanent|artifact|enchantment|land|nonland\s+permanent)",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ExileTarget";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ControlSpellFactory.ExileTargetSpell(ctx.Resolver, $"target {m.Groups[1].Value}")
            : null;
    }
}
