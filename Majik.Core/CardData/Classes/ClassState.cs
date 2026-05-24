using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Classes;

/// <summary>
/// CR 716 — Class enchantments. Start at level 1; activated level-up
/// abilities advance to the next level (CR 716.4 — sequential, "level up to
/// N" must be activated only when current level is N-1). Each level grants
/// its own static / triggered abilities; higher-level abilities stack on
/// top of lower-level ones.
///
/// State holder for a single Class permanent. Mirrors the
/// <see cref="Majik.Core.CardData.Sagas.SagaState"/> pattern — attached to
/// the permanent via <see cref="Permanent.AttachClassState"/>; per-card
/// factories register level-up activated abilities + per-level triggered
/// abilities that consult <see cref="CurrentLevel"/> via the
/// triggered-ability <c>interveningIf</c> gate (CR 603.4 — won't trigger if
/// the gate fails on event delivery).
///
/// <para>
/// <see cref="LevelUpCosts"/> is indexed by destination level minus two:
/// <c>LevelUpCosts[0]</c> pays "Level 1 → Level 2", <c>LevelUpCosts[1]</c>
/// pays "Level 2 → Level 3", and so on. Length must equal
/// <c>MaxLevel - 1</c>.
/// </para>
/// </summary>
public sealed class ClassState
{
    private readonly ManaCost[] _levelUpCosts;

    /// <summary>Maximum level this Class can reach (CR 716.1 — printed on
    /// the card; Stormchaser's Talent goes to 3).</summary>
    public int MaxLevel { get; }

    /// <summary>Current level of this Class. Starts at 1 (CR 716.2 —
    /// "a Class enchantment enters with no level counters; it is treated
    /// as a level-1 Class").</summary>
    public int CurrentLevel { get; private set; } = 1;

    /// <summary>
    /// CR 716.4 — per-level activation costs. Indexed by destination level
    /// minus two: <c>LevelUpCosts[N-2]</c> pays for "level up to N".
    /// </summary>
    public IReadOnlyList<ManaCost> LevelUpCosts => _levelUpCosts;

    /// <summary>Optional callback fired on every level-up transition. Wired
    /// by the per-card factory to publish a <see cref="ClassLevelUpEvent"/>
    /// against the live event bus.</summary>
    public Action<int, int>? OnLevelUp { get; set; }

    /// <summary>Construct a Class state with explicit per-level costs.
    /// <paramref name="levelUpCosts"/>.Length must equal
    /// <paramref name="maxLevel"/> - 1 (one cost per transition).</summary>
    public ClassState(int maxLevel, IReadOnlyList<ManaCost> levelUpCosts)
    {
        if (maxLevel < 1) throw new ArgumentOutOfRangeException(nameof(maxLevel));
        if (levelUpCosts == null) throw new ArgumentNullException(nameof(levelUpCosts));
        if (levelUpCosts.Count != maxLevel - 1)
        {
            throw new ArgumentException(
                $"Expected {maxLevel - 1} level-up costs for a {maxLevel}-level Class, " +
                $"got {levelUpCosts.Count}.",
                nameof(levelUpCosts));
        }

        MaxLevel = maxLevel;
        _levelUpCosts = levelUpCosts.ToArray();
    }

    /// <summary>Backward-compatible constructor — no costs, single-step
    /// leveling (used by the MVP state-only tests).</summary>
    public ClassState(int maxLevel = 3)
        : this(maxLevel, BuildEmptyCosts(maxLevel))
    {
    }

    private static ManaCost[] BuildEmptyCosts(int maxLevel)
    {
        if (maxLevel < 1) throw new ArgumentOutOfRangeException(nameof(maxLevel));
        var costs = new ManaCost[maxLevel - 1];
        for (var i = 0; i < costs.Length; i++) costs[i] = ManaCost.Zero;
        return costs;
    }

    /// <summary>CR 716.4 — the cost to advance from <see cref="CurrentLevel"/>
    /// to <see cref="CurrentLevel"/> + 1, or <c>null</c> if already at
    /// <see cref="MaxLevel"/>.</summary>
    public ManaCost? NextLevelCost()
        => CurrentLevel >= MaxLevel ? null : _levelUpCosts[CurrentLevel - 1];

    /// <summary>CR 716.4 — the cost to advance to <paramref name="targetLevel"/>.
    /// Sequential gate enforced in <see cref="LevelUpTo"/>; this getter is
    /// used by the activated-ability factory to wire fixed-cost
    /// <see cref="ManaCostCost"/>s per level.</summary>
    public ManaCost CostFor(int targetLevel)
    {
        if (targetLevel < 2 || targetLevel > MaxLevel)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLevel),
                $"targetLevel must be in [2, {MaxLevel}].");
        }
        return _levelUpCosts[targetLevel - 2];
    }

    public bool CanLevelUp() => CurrentLevel < MaxLevel;

    /// <summary>CR 716.4 — sequential level-up gate. Returns true iff
    /// <paramref name="targetLevel"/> == <see cref="CurrentLevel"/> + 1.</summary>
    public bool CanLevelUpTo(int targetLevel) =>
        targetLevel == CurrentLevel + 1 && targetLevel <= MaxLevel;

    /// <summary>Unconditional level-up (legacy API). Advances by 1.</summary>
    public bool LevelUp()
    {
        if (!CanLevelUp()) return false;
        var from = CurrentLevel;
        CurrentLevel++;
        OnLevelUp?.Invoke(from, CurrentLevel);
        return true;
    }

    /// <summary>CR 716.4 — advance to <paramref name="targetLevel"/>.
    /// Returns false if the sequential gate fails (e.g. attempt to skip
    /// from 1 to 3) without mutating state.</summary>
    public bool LevelUpTo(int targetLevel)
    {
        if (!CanLevelUpTo(targetLevel)) return false;
        var from = CurrentLevel;
        CurrentLevel = targetLevel;
        OnLevelUp?.Invoke(from, CurrentLevel);
        return true;
    }
}
