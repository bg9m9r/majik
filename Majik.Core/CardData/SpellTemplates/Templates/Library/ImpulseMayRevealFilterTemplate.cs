using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Zones;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// "May-reveal-filter" Impulse shape:
///
///   "[Look at|Reveal] the top N cards of your library. You may [reveal|put] a
///    [filter] card from among them [and put it] into your hand. Put the rest
///    [on the bottom of your library | into your graveyard]."
///
/// Cards: Ancient Stirrings, Commune with Nature, Commune with Dinosaurs,
/// Peer Through Depths, Board the Weatherlight, Adventurous Impulse + the
/// Reveal-and-rest-to-graveyard sub-family (Gather the Pack, Benefaction of
/// Rhonas, Tapping at the Window, etc.) + the dual-filter "and/or" variants
/// (In the Presence of Ages, Relentless Pursuit, Benefaction of Rhonas:
/// "may put a creature card and/or a land card from among them...").
///
/// Distinct from <see cref="LookAtTopPutOneInHandTemplate"/>: that one is
/// "put one of them" (mandatory pick, no filter). This pattern is "may reveal
/// a [filter]" (optional + filtered).
///
/// v1 stub: always-pick (caster always "may"), drop the filter — keep the
/// topmost card. Rest goes to the captured destination.
/// </summary>
public sealed class ImpulseMayRevealFilterTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"(?:look\s+at|reveal)\s+the\s+top\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten|x)\s+cards\s+of\s+your\s+library\.\s*you\s+may\s+(?:reveal|put)\s+(?:a|an)\s+(?<filter>[a-z][a-z0-9\s,'\-]{0,80}?\s+)?card(?:\s+and(?:/or|\s+or)\s+(?:a|an)\s+[a-z][a-z0-9\s,'\-]{0,80}?\s+card)?\s+from\s+among\s+them\s+(?:and\s+put\s+it\s+)?into\s+your\s+hand\.\s*(?:then\s+)?put\s+the\s+rest\s+(?<dest>on\s+the\s+bottom\s+of\s+your\s+library|into\s+your\s+graveyard)",
        RegexOptions.IgnoreCase);

    public int Priority => 52;
    public string Name => "ImpulseMayRevealFilter";
    public BotIntent Intent => BotIntent.Tutor | BotIntent.Cantrip;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string>
            {
                ["n"] = m.Groups["n"].Value,
                ["dest"] = m.Groups["dest"].Value.ToLowerInvariant().Contains("graveyard")
                    ? "graveyard" : "bottom",
            }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = SpellTemplateHelpers.WordToInt(@params["n"]);
        var dest = @params.GetValueOrDefault("dest", "bottom") == "graveyard"
            ? ZoneType.Graveyard
            : ZoneType.Library;
        return LibrarySpellFactory.LookAtTopPutOneInHandSpell(ctx.Caster, n, dest);
    }
}
