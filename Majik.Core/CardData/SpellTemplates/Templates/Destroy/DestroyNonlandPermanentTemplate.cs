using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyNonlandPermanentTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+nonland\s+permanent",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "DestroyNonlandPermanent";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        Pattern.IsMatch(ctx.Text)
            ? DestroySpellFactory.DestroyTargetSpell(
                ctx.Resolver, "target nonland permanent",
                c => !c.HasType(CardType.Land))
            : null;
}
