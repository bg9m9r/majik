using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyLandTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+land\b",
        RegexOptions.IgnoreCase);

    public int Priority => 30;
    public string Name => "DestroyLand";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? DestroySpellFactory.DestroyTargetSpell(
                ctx.Resolver, "target land",
                c => c.HasType(CardType.Land))
            : null;
}
