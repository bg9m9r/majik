namespace Majik.Server.Matches;

/// <summary>Server-authoritative pre-game dice roll. Re-rolls on tie
/// until a winner is determined. Range: 1..6 inclusive each die.</summary>
public sealed class DiceRoller
{
    private readonly IRandomSource _rng;

    public DiceRoller(IRandomSource rng)
    {
        _rng = rng;
    }

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
