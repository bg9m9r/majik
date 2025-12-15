namespace Majik.Core.Domain.Exceptions;

/// <summary>
/// Exception thrown when a game operation is attempted in an invalid game state.
/// </summary>
public class InvalidGameStateException : DomainException
{
    public InvalidGameStateException(string message) : base(message)
    {
    }

    public InvalidGameStateException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
