using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

public sealed class ReanimateFromGraveyardTemplate : ISpellTemplate
{
    // Captures "Return [up to N|X|two|three|...]? target [kind chain]? card[s]
    // [extra filters]? from your graveyard to your hand". Catches both
    // single-target (Raise Dead, Call to Mind) and multi-target / variable-N
    // (Soul Salvage, Death Denied, Macabre Waltz, Aphetto Dredging) variants.
    //
    // The runtime stub returns ONE chosen target card to its owner's hand — a
    // v1 simplification for multi-target wordings. The kind string is purely
    // a display label for the target request; the resolver does not enforce
    // it. Color/keyword/tribe restrictions in the oracle text ("green card",
    // "Goblin card", "with cycling") are also lossy at v1.
    private static readonly Regex Pattern = new(
        @"return\s+(?:(?:up\s+to\s+)?(?:one|two|three|four|five|six|seven|eight|nine|ten|x)\s+)?target\s+(?<kind>(?:[\w-]+(?:\s+(?:or|and)\s+[\w-]+)*)?)\s*cards?\s+(?:[\w\s,-]*?\s+)?from\s+(?:your|a|an\s+opponent'?s?)\s+graveyard\s+to\s+your\s+hand",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ReanimateFromGraveyard";
    public BotIntent Intent => BotIntent.Reanimate;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["kind"] = m.Groups["kind"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx) =>
        LibrarySpellFactory.ReanimateSpell(ctx.Resolver, @params["kind"]);
}
