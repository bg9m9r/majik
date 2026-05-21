using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates.Templates.Destroy;

public sealed class DestroyCreatureTemplate : ISpellTemplate
{
    // Matches "destroy target [modifiers]? creature" for arbitrary modifier
    // chains seen on real cards: hyphenated subtypes ("non-Merfolk", "non-Elf"),
    // combat-state modifiers ("tapped", "blocking", "attacking or blocking",
    // "blocked"), color modifiers ("black", "green or white", "monocolored"),
    // and stacked non-X non-Y forms. The runtime stub destroys the chosen
    // target regardless of subtype, which is the correct v1 semantic — the
    // legality check belongs in the target predicate (future work) but the
    // resolved effect is the same.
    private static readonly Regex Pattern = new(
        @"destroy\s+target\s+(?:(?:[\w-]+|or)\s*,?\s*){0,6}creature\b",
        RegexOptions.IgnoreCase);

    public int Priority => 30;
    public string Name => "DestroyCreature";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText) =>
        Pattern.IsMatch(oracleText) ? EmptyParams.Instance : null;

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        DestroySpellFactory.DestroyCreatureSpell(ctx.Resolver);
}
