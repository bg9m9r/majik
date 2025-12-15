using Majik.Core.Zones;

namespace Majik.Core.Domain.Exceptions;

/// <summary>
/// Exception thrown when an invalid zone transition is attempted.
/// </summary>
public class InvalidZoneTransitionException : DomainException
{
    public ZoneType FromZone { get; }
    public ZoneType ToZone { get; }

    public InvalidZoneTransitionException(ZoneType fromZone, ZoneType toZone, string? message = null)
        : base(message ?? $"Invalid zone transition from {fromZone} to {toZone}")
    {
        FromZone = fromZone;
        ToZone = toZone;
    }
}
