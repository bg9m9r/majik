namespace Majik.Server.Matches;

/// <summary>Test seam over the system RNG. NextInt mirrors
/// <c>RandomNumberGenerator.GetInt32(min, max)</c>: minInclusive..maxExclusive.</summary>
public interface IRandomSource
{
    int NextInt(int minInclusive, int maxExclusive);
}
