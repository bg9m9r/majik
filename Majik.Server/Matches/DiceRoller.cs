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
}
