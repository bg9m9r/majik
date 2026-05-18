namespace Majik.Core.Random;

/// <summary>
/// CR 100.6 — per-game source of randomness. Seedable for deterministic
/// replay (Phase 29). Single instance per game, threaded into every
/// operation that needs entropy (shuffle, dice, coin flip).
///
/// Wraps <see cref="System.Random"/> so future swaps (cryptographic RNG
/// for tournament play, recorded sequence for replay) only touch this
/// class.
/// </summary>
public sealed class GameRandom
{
    private readonly System.Random _rng;

    public int Seed { get; }

    public GameRandom(int? seed = null)
    {
        Seed = seed ?? System.Random.Shared.Next();
        _rng = new System.Random(Seed);
    }

    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    public int Next(int maxExclusive) => _rng.Next(maxExclusive);

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int Next(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);

    /// <summary>Coin flip: true/false 50/50.</summary>
    public bool FlipCoin() => _rng.Next(2) == 0;

    /// <summary>N-sided die: 1..N inclusive.</summary>
    public int RollDie(int sides)
    {
        if (sides < 1) throw new ArgumentOutOfRangeException(nameof(sides));
        return _rng.Next(1, sides + 1);
    }

    /// <summary>Fisher-Yates shuffle of the given list (mutates in place).</summary>
    public void Shuffle<T>(IList<T> list)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
