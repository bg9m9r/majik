using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class UntapTargetTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"untap\s+target\s+(permanent|creature|artifact|land|enchantment)",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "UntapTarget";

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        var m = Pattern.Match(ctx.Text);
        return m.Success
            ? ControlSpellFactory.UntapTargetSpell(ctx.Resolver, $"target {m.Groups[1].Value}")
            : null;
    }
}
