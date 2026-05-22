using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Control;

public sealed class ExileTargetTemplate : ISpellTemplate
{
    // Accepts an optional modifier chain between "target" and the noun
    // (creature / permanent / artifact / enchantment / land). The negative
    // lookahead `(?!\s+card)` keeps us out of graveyard-exile and hand/
    // library-exile space — those use different effects (zone-aware).
    //
    // Picks up combat-state modifiers (attacking, blocking, tapped),
    // color (nonblack/nonwhite/etc, black or red, monocolored,
    // multicolored, colorless), and tribe / type prefixes (Spirit,
    // nontoken, nonlegendary).
    private static readonly Regex Pattern = new(
        @"exile\s+target\s+(?:(?:[\w-]+|or)\s*,?\s*){0,4}(?<kind>creature|permanent|artifact|enchantment|land)\b(?!\s+card)",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "ExileTarget";
    public BotIntent Intent => BotIntent.Removal;

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
        ControlSpellFactory.ExileTargetSpell(ctx.Resolver, $"target {@params["kind"]}");
}
