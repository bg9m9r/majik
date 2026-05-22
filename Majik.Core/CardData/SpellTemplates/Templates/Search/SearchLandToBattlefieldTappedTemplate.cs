using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Search;

public sealed class SearchLandToBattlefieldTappedTemplate : ISpellTemplate
{
    // Accepts: "a basic land/land card ... put it/that card onto the
    // battlefield tapped" AND "[up to N | up to X | N] basic land cards
    // [or Gate cards / with different names]? ... put them/those cards
    // onto the battlefield tapped" (Cultivate, Harrow, Explosive
    // Vegetation, Nissa's Expedition, Boundless Realms, Reshape the
    // Earth, etc).
    //
    // Also accepts the Cultivate / Kodama's Reach shape — "put one onto
    // the battlefield tapped and the other into your hand" — by extending
    // the put-subject alternation to include "one". v1 stub still fetches
    // a single land; modelling "the other into your hand" as a second
    // ramp step is deferred (the first tapped land covers the common-case
    // ramp signal which is what the binder is checked for).
    //
    // v1 stub fetches ONE land regardless of "up to N" wording — a
    // simplification, but the bound spell resolves correctly for ramping
    // the first land which is the common-case relevance check.
    private static readonly Regex Pattern = new(
        @"search\s+your\s+library\s+for\s+(?:a|(?:up\s+to\s+)?(?:one|two|three|four|five|six|seven|eight|nine|ten|x)|any\s+number\s+of)\s+(?<kind>basic\s+land|land|basic\s+land[s]?\s+and(?:/or)?\s+[\w-]+)\s+cards?\b[^.]*put\s+(?:it|that\s+card|them|those\s+cards|one)\s+onto\s+the\s+battlefield\s+tapped",
        RegexOptions.IgnoreCase);

    public int Priority => 100;
    public string Name => "SearchLandToBattlefieldTapped";
    public BotIntent Intent => BotIntent.Ramp;

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
        SearchSpellFactory.SearchLandToBattlefieldSpell(ctx.Caster, @params["kind"], tapped: true);
}
