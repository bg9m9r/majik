namespace Majik.Server.Matches;

/// <summary>Seam for time — allows tests to inject a fake clock.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
