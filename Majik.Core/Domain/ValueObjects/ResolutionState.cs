namespace Majik.Core.Domain.ValueObjects;

/// <summary>
/// Value object representing the resolution state of a stack object.
/// Immutable encapsulation of resolution information.
/// </summary>
public class ResolutionState : IEquatable<ResolutionState>
{
    /// <summary>
    /// Whether the object is currently resolving.
    /// </summary>
    public bool IsResolving { get; }

    /// <summary>
    /// Timestamp when resolution started (if resolving).
    /// </summary>
    public DateTime? ResolvedAt { get; }

    private ResolutionState(bool isResolving, DateTime? resolvedAt)
    {
        IsResolving = isResolving;
        ResolvedAt = resolvedAt;
    }

    /// <summary>
    /// Create initial resolution state (not resolving).
    /// </summary>
    public static ResolutionState NotResolving()
    {
        return new ResolutionState(false, null);
    }

    /// <summary>
    /// Create resolution state (resolving).
    /// </summary>
    public static ResolutionState Resolving()
    {
        return new ResolutionState(true, DateTime.UtcNow);
    }

    /// <summary>
    /// Create resolution state (resolved).
    /// </summary>
    public static ResolutionState Resolved(DateTime resolvedAt)
    {
        return new ResolutionState(false, resolvedAt);
    }

    public bool Equals(ResolutionState? other)
    {
        if (other == null) return false;
        return IsResolving == other.IsResolving &&
               ResolvedAt == other.ResolvedAt;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ResolutionState);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsResolving, ResolvedAt);
    }

    public static bool operator ==(ResolutionState? left, ResolutionState? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(ResolutionState? left, ResolutionState? right)
    {
        return !(left == right);
    }
}
