namespace Majik.Server.Matches;

/// <summary>Server-authoritative pre-game dice roll. <see cref="RollSingle"/>
/// produces a single 1..6 value via the injected <see cref="IRandomSource"/>.
/// MatchService orchestrates per-player rolls and tie auto-reroll.</summary>
public sealed class DiceRoller
{
    private readonly IRandomSource _rng;

    public DiceRoller(IRandomSource rng)
    {
        _rng = rng;
    }

    public int RollSingle() => _rng.NextInt(1, 7);

    [Obsolete("Use RollSingle() and orchestrate per-player rolls in MatchService.")]
    public MatchRoll Roll(string creatorSub, string opponentSub)
    {
        while (true)
        {
            var c = _rng.NextInt(1, 7);
            var o = _rng.NextInt(1, 7);
            if (c == o) continue;
            var winner = c > o ? creatorSub : opponentSub;
            return new MatchRoll
            {
                CreatorRoll = c,
                OpponentRoll = o,
                WinnerSub = winner,
            };
        }
    }
}
