using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

/// <summary>
/// "Destroy target tapped creature." (Murderous Compulsion, {1}{B} Sorcery —
/// CR 701.7 / CR 109.5). The dedicated sibling of
/// <see cref="DestroyCreatureTemplate"/> for the TAPPED-creature target filter:
/// it binds the same Destroy verb but through the declarative
/// <c>tapped_creature</c> target filter
/// (<see cref="Majik.Core.CardData.Definitions.TargetFilters"/>), so the prod
/// cast path offers only tapped creatures and re-checks "still tapped" at
/// resolution (CR 608.2b). The generic DestroyCreatureTemplate would otherwise
/// match this text and drop the "tapped" restriction (it offers ANY creature),
/// so this template runs at a HIGHER priority to win the bind.
///
/// <para>The pattern is intentionally tight: "destroy target tapped creature"
/// with no other modifiers, so colour / subtype variants ("destroy target
/// tapped black creature") still fall through to the broader template rather
/// than being silently narrowed here.</para>
/// </summary>
public sealed class DestroyTappedCreatureTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"^destroy\s+target\s+tapped\s+creature\b",
        RegexOptions.IgnoreCase);

    // Above DestroyCreatureTemplate (30) so the tapped-filtered bind wins.
    public int Priority => 31;
    public string Name => "DestroyTappedCreature";
    public BotIntent Intent => BotIntent.Removal;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText.TrimStart()) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DestroySpellFactory.DestroyTappedCreatureSpell(ctx.Resolver);
}
