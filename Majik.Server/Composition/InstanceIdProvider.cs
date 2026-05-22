namespace Majik.Server.Composition;

/// <summary>
/// Stable per-process identifier. Prefers Render's <c>RENDER_INSTANCE_ID</c>
/// env var (set per instance) so different replicas have distinguishable
/// ids in logs + Redis ownership records; falls back to a fresh Guid when
/// the env var is missing (local dev, tests).
/// </summary>
public interface IInstanceIdProvider
{
    string Value { get; }
}

public sealed class InstanceIdProvider : IInstanceIdProvider
{
    public string Value { get; }

    public InstanceIdProvider()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID");
        Value = string.IsNullOrWhiteSpace(fromEnv) ? Guid.NewGuid().ToString("N") : fromEnv;
    }

    /// <summary>Explicit-value constructor — for tests.</summary>
    public InstanceIdProvider(string value)
    {
        Value = value;
    }
}
