namespace Majik.Core.CardData.Classes;

/// <summary>
/// CR 716 — Class enchantments. Start at level 1; activated level-up
/// abilities advance to the next level (must be sequential). Each level
/// grants its own static/triggered abilities; higher-level abilities
/// stack on top of lower-level ones.
///
/// MVP state: tracks current level; level-up validates sequential.
/// Per-level abilities are wired by the caller into the layer system /
/// trigger manager based on <see cref="CurrentLevel"/>.
/// </summary>
public sealed class ClassState
{
    public int MaxLevel { get; }
    public int CurrentLevel { get; private set; } = 1;

    public ClassState(int maxLevel = 3)
    {
        if (maxLevel < 1) throw new ArgumentOutOfRangeException(nameof(maxLevel));
        MaxLevel = maxLevel;
    }

    public bool CanLevelUp() => CurrentLevel < MaxLevel;

    public bool LevelUp()
    {
        if (!CanLevelUp()) return false;
        CurrentLevel++;
        return true;
    }
}
