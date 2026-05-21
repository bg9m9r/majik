using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+(non\w+\s+)?creature",
        RegexOptions.IgnoreCase);

    public int Priority => 30;
    public string Name => "DestroyCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? DestroySpellFactory.DestroyCreatureSpell(ctx.Resolver)
            : null;
}
