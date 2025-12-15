using Majik.Core.Cards;

namespace Majik.Core.Combat;

/// <summary>
/// Helper methods for checking combat-related abilities on creatures.
/// </summary>
public static class CombatAbilities
{
    /// <summary>
    /// Check if a creature has first strike.
    /// </summary>
    public static bool HasFirstStrike(Creature creature)
    {
        if (creature == null) return false;
        
        // TODO: Check for first strike ability via StaticAbilityManager
        // For now, return false - will be enhanced when static abilities are fully implemented
        return false;
    }

    /// <summary>
    /// Check if a creature has double strike.
    /// </summary>
    public static bool HasDoubleStrike(Creature creature)
    {
        if (creature == null) return false;
        
        // TODO: Check for double strike ability via StaticAbilityManager
        return false;
    }

    /// <summary>
    /// Check if a creature has trample.
    /// </summary>
    public static bool HasTrample(Creature creature)
    {
        if (creature == null) return false;
        
        // TODO: Check for trample ability via StaticAbilityManager
        return false;
    }

    /// <summary>
    /// Check if a creature has deathtouch.
    /// </summary>
    public static bool HasDeathtouch(Creature creature)
    {
        if (creature == null) return false;
        
        // TODO: Check for deathtouch ability via StaticAbilityManager
        return false;
    }

    /// <summary>
    /// Check if a creature has vigilance.
    /// </summary>
    public static bool HasVigilance(Creature creature)
    {
        if (creature == null) return false;
        
        // TODO: Check for vigilance ability via StaticAbilityManager
        return false;
    }

    /// <summary>
    /// Check if a creature has haste.
    /// </summary>
    public static bool HasHaste(Creature creature)
    {
        if (creature == null) return false;
        
        // TODO: Check for haste ability via StaticAbilityManager
        return false;
    }

    /// <summary>
    /// Check if a creature has reach.
    /// </summary>
    public static bool HasReach(Creature creature)
    {
        if (creature == null) return false;
        
        // TODO: Check for reach ability via StaticAbilityManager
        return false;
    }

    /// <summary>
    /// Check if a creature has flying.
    /// </summary>
    public static bool HasFlying(Creature creature)
    {
        if (creature == null) return false;
        
        // TODO: Check for flying ability via StaticAbilityManager
        return false;
    }

    /// <summary>
    /// Check if a creature can block a creature with flying.
    /// </summary>
    public static bool CanBlockFlying(Creature creature)
    {
        if (creature == null) return false;
        
        return HasFlying(creature) || HasReach(creature);
    }
}
