using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// "May-reveal-filter" Impulse shape — Ancient Stirrings, Commune with Nature,
/// Commune with Dinosaurs, Peer Through Depths, Board the Weatherlight,
/// Adventurous Impulse, etc.:
///
///   "Look at the top N cards of your library. You may reveal a [filter] card
///    from among them and put it into your hand. Put the rest on the bottom
///    of your library [in any|random order]."
///
/// Distinct from <see cref="LookAtTopPutOneInHandTemplate"/>: that one is
/// "put one of them" (mandatory pick, no filter). This pattern is "may reveal
/// a [filter]" (optional + filtered).
///
/// v1 stub: always-pick (caster always "may"), drop the filter — keep the
/// topmost card. Rest goes to bottom.
/// </summary>
public sealed class ImpulseMayRevealFilterTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"look\s+at\s+the\s+top\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten|x)\s+cards\s+of\s+your\s+library\.\s*you\s+may\s+reveal\s+(?:a|an)\s+(?<filter>[a-z][a-z0-9\s,'\-]{0,80}?\s+)?card\s+from\s+among\s+them\s+and\s+put\s+it\s+into\s+your\s+hand\.\s*(?:then\s+)?put\s+the\s+rest\s+on\s+the\s+bottom\s+of\s+your\s+library",
        RegexOptions.IgnoreCase);

    public int Priority => 52;
    public string Name => "ImpulseMayRevealFilter";

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string> { ["n"] = m.Groups["n"].Value }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = SpellTemplateHelpers.WordToInt(@params["n"]);
        return LibrarySpellFactory.LookAtTopPutOneInHandSpell(ctx.Caster, n, ZoneType.Library);
    }
}
