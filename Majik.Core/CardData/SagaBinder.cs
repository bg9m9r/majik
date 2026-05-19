using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.Sagas;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData;

/// <summary>
/// CR 714 — Saga binder. Detects Saga-subtype permanents, parses the
/// chapter list from their oracle text ("I —", "II —", "III, IV —",
/// etc.) to determine the final chapter number, and attaches a
/// <see cref="SagaState"/> with a generic per-chapter callback.
///
/// MVP chapter effects:
///   - Urza's Saga (hardcoded by name): I+II → spawn a 2/2 Construct
///     artifact creature token; III → no-op (tutor not yet wired).
///   - All other Sagas: chapter callback is a no-op (per-card effect
///     parsing is a future cut). The state still ticks so SBA
///     sacrifices the Saga after the final chapter.
/// </summary>
public static class SagaBinder
{
    private static readonly Regex ChapterMarker = new(
        @"\b(?<r>I{1,3}V?|IV|V{1,3}I?|IX|X)\s*[—,–]",
        RegexOptions.IgnoreCase);

    public static bool Bind(ICard card, CardEntity entity)
    {
        if (card == null) throw new ArgumentNullException(nameof(card));
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (card is not Permanent perm) return false;
        if (!card.HasSubtype(CardSubtype.Saga)) return false;

        var text = entity.OracleText ?? string.Empty;
        var finalChapter = ParseFinalChapter(text);
        if (finalChapter < 1) finalChapter = 3; // safe default

        Action<int> onChapter = card.Name switch
        {
            "Urza's Saga" => chapter =>
            {
                if (chapter == 1 || chapter == 2)
                {
                    Majik.Core.Tokens.TokenFactory.CreateOnBattlefield(
                        new Majik.Core.Tokens.TokenFactory.TokenSpec(
                            "Construct", 2, 2,
                            Subtypes: new[] { CardSubtype.Construct }),
                        perm.Controller ?? perm.Owner!);
                }
                // Chapter III: tutor for artifact, deferred.
            },
            _ => _ => { /* generic saga — no-op effect, state still ticks */ },
        };

        perm.SagaState = new SagaState(perm, finalChapter, onChapter);
        return true;
    }

    private static int ParseFinalChapter(string oracleText)
    {
        var max = 0;
        foreach (Match m in ChapterMarker.Matches(oracleText))
        {
            var roman = m.Groups["r"].Value.ToUpperInvariant();
            // Multi-chapter markers like "II, III —" set max via both.
            foreach (var part in roman.Split(','))
            {
                var n = RomanToInt(part.Trim());
                if (n > max) max = n;
            }
        }
        return max;
    }

    private static int RomanToInt(string s) => s switch
    {
        "I" => 1, "II" => 2, "III" => 3, "IV" => 4, "V" => 5,
        "VI" => 6, "VII" => 7, "VIII" => 8, "IX" => 9, "X" => 10,
        _ => 0,
    };
}
