namespace Majik.Core.Domain.Exceptions;

/// <summary>
/// Exception thrown when a player attempts an invalid action.
/// </summary>
public class InvalidPlayerActionException : DomainException
{
    public InvalidPlayerActionException(string message) : base(message)
    {
    }

    public InvalidPlayerActionException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
