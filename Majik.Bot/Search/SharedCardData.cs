using Majik.Core.CardData;

namespace Majik.Bot.Search;

/// <summary>
/// The bot's single shared <see cref="EmbeddedCardRepository"/> instance.
/// EmbeddedCardRepository loads its 22k-row gz seed lazily on first
/// <c>GetByName</c>, so constructing it is cheap and ONE shared instance
/// avoids re-reading the seed per consumer. Reads are thread-safe under
/// concurrent search (lookup over an immutable in-memory dictionary).
///
/// <para>Consumers: <see cref="EngineSimulator"/> (sandbox spell-definition
/// resolver), <see cref="DirectDamageRecognizer"/> (oracle-text reach scan),
/// <see cref="DeterminizationSampler"/> (sampled-card construction).</para>
/// </summary>
internal static class SharedCardData
{
    private static readonly Lazy<EmbeddedCardRepository> LazyRepo =
        new(() => new EmbeddedCardRepository());

    /// <summary>The shared repo instance (created on first touch).</summary>
    internal static EmbeddedCardRepository Repo => LazyRepo.Value;
}
