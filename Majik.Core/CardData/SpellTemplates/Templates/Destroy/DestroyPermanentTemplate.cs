using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyPermanentTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+permanent",
        RegexOptions.IgnoreCase);

    public int Priority => 10;
    public string Name => "DestroyPermanent";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? DestroySpellFactory.DestroyTargetSpell(
                ctx.Resolver, "target permanent", _ => true)
            : null;
}
