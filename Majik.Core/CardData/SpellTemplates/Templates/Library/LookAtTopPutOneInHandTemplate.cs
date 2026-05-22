using System.Text.RegularExpressions;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Game;
using Majik.Core.Zones;

namespace Majik.Core.CardData.SpellTemplates.Templates.Library;

/// <summary>
/// "Impulse" template — "Look at the top N cards of your library. Put
/// one of them into your hand and the rest [on the bottom of your
/// library | into your graveyard][, in any/random order]."
///
/// Covers Impulse, Anticipate, Sleight of Hand, Telling Time (close
/// enough — falls back to bottom), Strategic Planning, Forbidden
/// Alchemy, Flash of Insight, Dig Through Time, Dark Bargain (cantrip
/// half lossy), and similar.
///
/// v1 stub: take top N, put the topmost into hand, push the rest to the
/// indicated destination. No agent prompt — deterministic pick. "In any
/// order" / "random order" are no-ops at v1.
/// </summary>
public sealed class LookAtTopPutOneInHandTemplate : ISpellTemplate
{
    // Accept both "one of them" (Impulse, Anticipate) and "one of those cards"
    // (Accumulate Wisdom, See the Truth) — same effect, two oracle wordings.
    private static readonly Regex Pattern = new(
        @"look\s+at\s+the\s+top\s+(?<n>\d+|one|two|three|four|five|six|seven|eight|nine|ten|x)\s+cards\s+of\s+your\s+library\.\s*put\s+one\s+of\s+(?:them|those\s+cards)\s+into\s+your\s+hand\s+and\s+the\s+rest\s+(?<dest>on\s+the\s+bottom\s+of\s+your\s+library|into\s+your\s+graveyard)",
        RegexOptions.IgnoreCase);

    public int Priority => 50;
    public string Name => "LookAtTopPutOneInHand";

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
        var dest = @params["dest"] == "graveyard" ? ZoneType.Graveyard : ZoneType.Library;
        return LibrarySpellFactory.LookAtTopPutOneInHandSpell(ctx.Caster, n, dest);
    }
}
