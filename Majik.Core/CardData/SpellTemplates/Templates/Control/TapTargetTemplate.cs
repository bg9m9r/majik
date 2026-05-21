using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class TapTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"\btap\s+target\s+(permanent|creature|artifact|land|enchantment|planeswalker)",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "TapTarget";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ControlSpellFactory.TapTargetSpell(ctx.Resolver, $"target {m.Groups[1].Value}")
            : null;
    }
}
