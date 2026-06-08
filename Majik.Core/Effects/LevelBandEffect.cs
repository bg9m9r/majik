using Majik.Core.Cards;
using Majik.Core.Counters;

namespace Majik.Core.Effects;

/// <summary>
/// CR 711 / CR 107.8 — Layer 7b set-base P/T for a leveler card's current
/// level band. A leveler's printed level symbols
/// (<c>{LEVEL N1-N2}</c> / <c>{LEVEL N3+}</c>) create static abilities of
/// the form "As long as this creature has at least N1 (but no more than N2)
/// level counters on it, it has base power and toughness [P/T] …"
/// (CR 107.8a/b). This effect is the P/T half of those band statics: it
/// reads the source's live level-counter count each
/// <see cref="ContinuousEffectsService.Compute(Permanent)"/> pass and SETS
/// the working base P/T (Layer 7b — CR 613.7b) to the band whose
/// <c>[min, max]</c> counter range currently applies.
///
/// <para>Bands are non-cumulative (CR 107.8 — exactly one band applies at a
/// time; the highest band whose range contains the current count wins). The
/// level-0 band (no level counters) is INTENTIONALLY NOT modeled here — the
/// printed P/T above the first level symbol is the creature's base P/T while
/// it has no level counters, so this effect simply stays
/// <see cref="IsActive"/>=false (and the printed base P/T stands) until the
/// first counter lands.</para>
///
/// <para>The keyword/ability half of each band static is wired separately by
/// the leveler's factory (a <see cref="GrantAbilityEffect"/> /
/// <see cref="GrantAbilityToGroupStaticEffect"/> whose band gate matches the
/// same counter range), because abilities materialise on the bearer's
/// <see cref="Card.Abilities"/> list rather than on the P/T working set.</para>
///
/// <para>Layer 7c counter arithmetic (+1/+1 / -1/-1) runs AFTER this 7b set
/// (CR 613.7), so a level counter does NOT itself change P/T — only the band
/// it pushes the creature into does — and any real +1/+1 counters stack on
/// top of the band's base.</para>
/// </summary>
public sealed class LevelBandEffect : ContinuousEffect
{
    /// <summary>
    /// One <c>{LEVEL N1-N2}</c> / <c>{LEVEL N3+}</c> band: the inclusive
    /// level-counter range and the base P/T it sets (CR 107.8a/b).
    /// <paramref name="MaxLevel"/> is <see cref="int.MaxValue"/> for an
    /// open-ended <c>{LEVEL N+}</c> band.
    /// </summary>
    public readonly record struct Band(int MinLevel, int MaxLevel, int Power, int Toughness);

    private readonly Creature _source;
    private readonly IReadOnlyList<Band> _bands;

    public LevelBandEffect(Creature source, IReadOnlyList<Band> bands)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _bands = bands ?? throw new ArgumentNullException(nameof(bands));
    }

    public override Layer Layer => Layer.PT_SetBase;

    public override Permanent? Source => _source;

    /// <summary>The current level-counter count on the source (CR 711.2).</summary>
    private int Level => _source.Counters.Count(CounterType.Level);

    /// <summary>
    /// CR 107.8 — the band whose inclusive range contains the current level,
    /// or null when no band applies (level 0 / below the first band's
    /// minimum — the printed base P/T stands).
    /// </summary>
    private Band? ActiveBand()
    {
        Band? match = null;
        foreach (var band in _bands)
        {
            if (Level >= band.MinLevel && Level <= band.MaxLevel)
            {
                match = band;
            }
        }
        return match;
    }

    public override bool IsActive() =>
        _source.Zone == Majik.Core.Zones.ZoneType.Battlefield && ActiveBand() != null;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _source);

    public override void Apply(CreatureCharacteristics chars)
    {
        if (ActiveBand() is { } band)
        {
            chars.Power = band.Power;
            chars.Toughness = band.Toughness;
        }
    }

    /// <summary>
    /// Sim-only: reconstruct an identical <see cref="LevelBandEffect"/> bound to
    /// <paramref name="clonedSource"/> for the search-sandbox clone.
    /// The level-counter count is read live from the cloned source's counters
    /// (counters are value-cloned by <see cref="Majik.Core.Simulation.GameStateCloner"/>),
    /// so the band resolves correctly without any extra wiring.
    /// preserves: _bands; source → clonedSource (as Creature).
    /// </summary>
    internal override ContinuousEffect? CloneForSim(
        Permanent clonedSource,
        System.Func<System.Collections.Generic.IReadOnlyList<Majik.Core.Players.Player>>? clonedPlayers)
        => clonedSource is Creature clonedCreature
            ? new LevelBandEffect(clonedCreature, _bands)
            : null;
}
