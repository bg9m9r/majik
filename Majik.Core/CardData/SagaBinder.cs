using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.Sagas;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData;

/// <summary>
/// CR 714 — Saga binder. Detects Saga-subtype permanents, parses the
/// chapter list from their oracle text ("I —", "II —", "III, IV —",
/// etc.) to determine the final chapter number, and attaches a
/// <see cref="SagaState"/> with a generic per-chapter callback.
///
/// Per-card chapter effects (hardcoded by card.Name):
///   - Urza's Saga: I+II → spawn a 2/2 Construct artifact creature
///     token; III → tutor, deferred.
///   - Fable of the Mirror-Breaker (// Reflection of Kiki-Jiki): I →
///     spawn a 2/2 red Goblin Shaman token (embedded "attacks → Treasure"
///     trigger deferred); II → discard up to 2, draw that many (v1
///     pumps the first two cards in hand and draws 2; "you may" opt-out
///     deferred); III → transform, deferred.
///   - The Legend of Roku (// Avatar Roku): I → exile top 3 of library
///     (the "may play those cards until end of next turn" rider is
///     deferred — needs an alt-play / temporal-permission framework);
///     II → add one mana of any color (v1 picks {R} deterministically —
///     no mana-color prompt yet); III → transform, deferred.
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
            "Fable of the Mirror-Breaker"
                or "Fable of the Mirror-Breaker // Reflection of Kiki-Jiki"
                => MakeFableChapterHandler(perm),
            "The Legend of Roku"
                or "The Legend of Roku // Avatar Roku"
                => MakeRokuChapterHandler(perm),
            _ => _ => { /* generic saga — no-op effect, state still ticks */ },
        };

        perm.SagaState = new SagaState(perm, finalChapter, onChapter);
        return true;
    }

    /// <summary>
    /// Fable of the Mirror-Breaker (NEO, {2}{R}).
    /// I — Create a 2/2 red Goblin Shaman creature token with "Whenever this
    ///     creature attacks, create a Treasure token."  v1 creates the token
    ///     body; the embedded attack trigger is deferred (no attack-trigger
    ///     wiring for token-resident abilities yet).
    /// II — You may discard up to two cards. If you do, draw that many cards.
    ///     v1: discard up to the first two cards in hand and draw exactly
    ///     that many. "You may" opt-out + per-card choice deferred.
    /// III — Exile this Saga, then return it transformed (Reflection of
    ///     Kiki-Jiki). Deferred — transform infrastructure for sagas is not
    ///     wired (CR 714.4 / 712.4); per task scope, no transform built.
    /// </summary>
    private static Action<int> MakeFableChapterHandler(Permanent perm) => chapter =>
    {
        var controller = perm.Controller ?? perm.Owner!;
        switch (chapter)
        {
            case 1:
                Majik.Core.Tokens.TokenFactory.CreateOnBattlefield(
                    new Majik.Core.Tokens.TokenFactory.TokenSpec(
                        "Goblin Shaman", 2, 2,
                        Subtypes: new[] { CardSubtype.Goblin, CardSubtype.Shaman }),
                    controller);
                break;
            case 2:
                DiscardUpToAndDraw(controller, max: 2);
                break;
            // case 3: transform — deferred.
        }
    };

    /// <summary>
    /// The Legend of Roku (TLA, {2}{R}{R}).
    /// I — Exile the top three cards of your library. Until the end of your
    ///     next turn, you may play those cards. v1: cards move to exile;
    ///     the "you may play them" rider is deferred (no alt-play /
    ///     turn-scoped permission system yet).
    /// II — Add one mana of any color. v1: adds {R} deterministically —
    ///     no mana-color prompt; matches the deck's red theme.
    /// III — Exile this Saga, then return it transformed (Avatar Roku).
    ///     Deferred — transform infrastructure for sagas not wired.
    /// </summary>
    private static Action<int> MakeRokuChapterHandler(Permanent perm) => chapter =>
    {
        var controller = perm.Controller ?? perm.Owner!;
        switch (chapter)
        {
            case 1:
                ExileTopOfLibrary(controller, n: 3);
                break;
            case 2:
                controller.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("R"));
                break;
            // case 3: transform — deferred.
        }
    };

    /// <summary>CR 701.7 — discard up to <paramref name="max"/> cards from
    /// the front of <paramref name="player"/>'s hand and draw the same
    /// number. v1: deterministic (no agent prompt). Player-choice opt-out
    /// ("you may") is deferred.</summary>
    private static void DiscardUpToAndDraw(Player player, int max)
    {
        var hand = player.Zones.Hand.GetCards().Take(max).ToList();
        foreach (var card in hand)
        {
            player.Zones.Hand.RemoveCard(card);
            player.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }

        var count = hand.Count;
        for (var i = 0; i < count; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null)
            {
                player.MarkTriedToDrawFromEmptyLibrary();
                return;
            }
            player.Zones.Library.RemoveCard(top);
            player.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        }
    }

    /// <summary>Move the top <paramref name="n"/> cards of
    /// <paramref name="player"/>'s library to their exile zone. Stops
    /// short silently if the library runs out.</summary>
    private static void ExileTopOfLibrary(Player player, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var top = player.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            player.Zones.Library.RemoveCard(top);
            player.Zones.Exile.AddCard(top);
            top.SetZone(ZoneType.Exile);
        }
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
