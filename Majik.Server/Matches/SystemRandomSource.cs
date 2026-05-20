using System.Security.Cryptography;

namespace Majik.Server.Matches;

public sealed class SystemRandomSource : IRandomSource
{
    public int NextInt(int minInclusive, int maxExclusive) =>
        RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);
}
