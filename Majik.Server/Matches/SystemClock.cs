namespace Majik.Server.Matches;

/// <summary>Production clock backed by <see cref="DateTime.UtcNow"/>.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
