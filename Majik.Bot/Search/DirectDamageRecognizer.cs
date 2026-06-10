using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Majik.Core.CardData;
using Majik.Core.Cards;

namespace Majik.Bot.Search;

/// <summary>
/// Recognizes how much DIRECT damage a card can deal to a player, from oracle text.
/// Deliberately narrow: "deals N damage to any target / target player / each opponent"
/// patterns (the burn-reach class) — creatures/combat are covered by board eval terms.
/// Cached per card name (oracle text is static per name). Modal cards: max matching mode.
///
/// <para><b>Oracle text source:</b> built <see cref="ICard"/> instances do NOT carry
/// oracle text (only <see cref="CardEntity"/> does, and <see cref="ScryfallCardFactory"/>
/// consumes it at bind time without storing it on the card), so the recognizer looks
/// the text up by name via a shared lazy <see cref="EmbeddedCardRepository"/> — the
/// same default-instance pattern as <see cref="DeterminizationSampler"/>. The lookup
/// happens at most ONCE per distinct card name; every later call is a cache hit.</para>
/// </summary>
internal static class DirectDamageRecognizer
{
    /// <summary>
    /// Shared lazy repo for the oracle-text lookup. EmbeddedCardRepository loads its
    /// 22k-row seed lazily on first GetByName; one shared instance avoids re-reading
    /// the gz. Safe under concurrent DamageToPlayer calls (parallel MCTS rollouts):
    /// GetByName is read-only over the immutable in-memory dictionary.
    /// </summary>
    private static readonly Lazy<EmbeddedCardRepository> Repo = new(() => new EmbeddedCardRepository());

    /// <summary>Per-name result cache — oracle text is static per name, so the scan runs once.</summary>
    private static readonly ConcurrentDictionary<string, int> Cache = new();

    /// <summary>
    /// The burn-reach class: "deals N damage to" followed by a target phrase that can
    /// hit a PLAYER. Creature-only phrasings ("target creature", "each creature", ...)
    /// deliberately do not match.
    /// </summary>
    private static readonly Regex DamagePattern = new(
        @"deals (\d+) damage to (any target|target player|each opponent|target player or planeswalker|each player)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The maximum direct damage <paramref name="card"/> can deal to a player per the
    /// recognized patterns, or 0 when none match (creatures, lands, creature-only burn).
    /// </summary>
    public static int DamageToPlayer(ICard card) =>
        Cache.GetOrAdd(card.Name, static name => Scan(Repo.Value.GetByName(name)?.OracleText));

    /// <summary>
    /// Scan oracle text for the burn-reach patterns; modal cards (multiple matches)
    /// yield the MAX matching mode. Null/empty text → 0.
    /// </summary>
    private static int Scan(string? oracleText)
    {
        if (string.IsNullOrEmpty(oracleText))
            return 0;

        var max = 0;
        foreach (Match m in DamagePattern.Matches(oracleText))
            max = Math.Max(max, int.Parse(m.Groups[1].Value));
        return max;
    }
}
