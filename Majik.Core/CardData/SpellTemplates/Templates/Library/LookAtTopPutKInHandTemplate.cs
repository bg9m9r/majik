using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Zones;

using Majik.Core.Cards;
namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// K-keep variant of <see cref="LookAtTopPutOneInHandTemplate"/>:
///
///   "Look at the top N cards of your library. Put K of them into your hand
///    and the rest [on the bottom of your library | into your graveyard]."
///
/// Cards: Bitter Revelation (look-4-keep-2, rest→graveyard), Blood Price
/// (look-4-keep-2, rest→bottom), Rakshasa's Bargain (look-4-keep-2,
/// rest→graveyard) and similar. Trailing rider clauses ("You lose N life")
/// are dropped at v1 — the look+keep is the load-bearing effect.
/// </summary>
public sealed class LookAtTopPutKInHandTemplate : ISpellTemplate
{
    private static readonly Regex Pattern = new(
        @"look\s+at\s+the\s+top\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten|x)\s+cards\s+of\s+your\s+library\.\s*put\s+(?<k>\d+|two|three|four|five)\s+of\s+(?:them|those\s+cards)\s+into\s+your\s+hand\s+and\s+the\s+rest\s+(?<dest>on\s+the\s+bottom\s+of\s+your\s+library|into\s+your\s+graveyard)",
        RegexOptions.IgnoreCase);

    // Priority above the single-keep template so the K-keep wording binds first.
    public int Priority => 52;
    public string Name => "LookAtTopPutKInHand";
    public BotIntent Intent => BotIntent.Cantrip;

    public SpellDefinition? TryBind(SpellBindContext ctx) =>
        SpellTemplateBindHelper.DefaultTryBind(this, ctx);

    public IReadOnlyDictionary<string, string>? TryExtractParams(string oracleText)
    {
        var m = Pattern.Match(oracleText);
        return m.Success
            ? new Dictionary<string, string>
            {
                ["n"] = m.Groups["n"].Value,
                ["k"] = m.Groups["k"].Value,
                ["dest"] = m.Groups["dest"].Value.ToLowerInvariant().Contains("graveyard")
                    ? "graveyard" : "bottom",
            }
            : null;
    }

    public SpellDefinition Rehydrate(IReadOnlyDictionary<string, string> @params, SpellBindContext ctx)
    {
        var n = SpellTemplateHelpers.WordToInt(@params["n"]);
        var k = SpellTemplateHelpers.WordToInt(@params["k"]);
        var dest = @params["dest"] == "graveyard" ? ZoneType.Graveyard : ZoneType.Library;
        return LibrarySpellFactory.LookAtTopPutKInHandSpell(ctx.Caster, n, k, dest);
    }
}
