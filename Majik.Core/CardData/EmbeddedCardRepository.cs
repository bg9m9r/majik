using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Majik.Core.Cards;

namespace Majik.Core.CardData;

/// <summary>
/// <see cref="ICardRepository"/> backed by a gzipped JSON resource
/// embedded in the <c>Majik.Core</c> assembly (one row per Modern-legal
/// card name, ~22k rows, ~1.9 MB gzipped). The seed is loaded lazily on
/// first access into an in-memory dictionary keyed by <c>Name</c>
/// (case-insensitive); subsequent lookups are O(1).
///
/// Replaces the previous <c>DbCardRepository</c> / <c>HttpCardRepository</c>
/// chain — no SQLite, no out-of-process HTTP hop. The <c>IsImplemented</c>
/// flag is <b>derived at load time</b> from the <c>[CardName]</c> factory
/// registry (see <see cref="Factories.ImplementedCardNames"/>), overriding
/// whatever value was baked into the gzipped seed — so adding a factory
/// flips the flag without regenerating the binary seed, and runtime
/// mutation stays intentionally unsupported.
/// Thread-safe via <see cref="Lazy{T}"/> with
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>.
/// </summary>
public sealed class EmbeddedCardRepository : ICardRepository
{
    /// <summary>Default location of the embedded gzipped seed inside
    /// <c>Majik.Core</c>. Marked <c>internal</c> so tests can reference
    /// it without leaking the implementation detail elsewhere.</summary>
    internal const string DefaultResourceName =
        "Majik.Core.CardData.Embedded.modern-cards.json.gz";

    private readonly Lazy<IReadOnlyDictionary<string, CardEntity>> _byName;
    private readonly Lazy<IReadOnlyList<CardEntity>> _ordered;
    private readonly ILogSink _log;

    public EmbeddedCardRepository() : this(LoadFromEmbeddedResource, NullLog.Instance)
    {
    }

    /// <summary>Test seam — pass a custom dictionary or a custom
    /// log sink. Used by unit tests to avoid loading the full 22k
    /// embedded pool.</summary>
    internal EmbeddedCardRepository(
        Func<IReadOnlyList<CardEntity>> loader,
        ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _log = log ?? NullLog.Instance;
        _ordered = new Lazy<IReadOnlyList<CardEntity>>(
            loader,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _byName = new Lazy<IReadOnlyDictionary<string, CardEntity>>(
            () => BuildIndex(_ordered.Value),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static IReadOnlyDictionary<string, CardEntity> BuildIndex(
        IReadOnlyList<CardEntity> rows)
    {
        var dict = new Dictionary<string, CardEntity>(
            rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            // Last-write-wins on duplicate names — the export script
            // already deduplicates by name, so collisions here would be
            // a regeneration bug. Silent overwrite mirrors the old
            // DbCardRepository.GetByName behaviour (FirstOrDefault on
            // an indexed Name column).
            dict[row.Name] = row;
        }
        return dict;
    }

    public CardEntity? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        // Reprint-name aliasing (Secret Lair / Universes Beyond renames),
        // same call site as the prior DbCardRepository.
        var lookup = CardNameAliases.Resolve(name);

        if (_byName.Value.TryGetValue(lookup, out var hit)) return hit;

        // Double-faced cards (CR 712) are stored as "Front // Back".
        // Walk the ordered list once to find any "lookup // ..." entry.
        // Linear scan is acceptable: the embedded pool is bounded
        // (~22k rows) and this branch only fires on a primary-name miss.
        var prefix = lookup + " // ";
        foreach (var row in _ordered.Value)
        {
            if (row.Name.StartsWith(prefix, StringComparison.Ordinal))
                return row;
        }
        return null;
    }

    public IReadOnlyList<CardEntity> Search(
        string? q,
        bool implementedOnly,
        int limit,
        IReadOnlyList<string>? colors = null,
        IReadOnlyList<string>? types = null,
        IReadOnlyList<int>? cmcBuckets = null)
    {
        if (limit < 1) return Array.Empty<CardEntity>();

        IEnumerable<CardEntity> stream = _ordered.Value;

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            stream = stream.Where(c =>
                c.Name.StartsWith(needle, StringComparison.OrdinalIgnoreCase));
        }
        if (implementedOnly)
            stream = stream.Where(c => c.IsImplemented);

        if (colors is { Count: > 0 })
        {
            var colorSet = colors.ToHashSet(StringComparer.OrdinalIgnoreCase);
            stream = stream.Where(c => MatchesColors(c, colorSet));
        }
        if (types is { Count: > 0 })
        {
            var typeSet = types.ToHashSet(StringComparer.OrdinalIgnoreCase);
            stream = stream.Where(c => MatchesTypes(c, typeSet));
        }
        if (cmcBuckets is { Count: > 0 })
        {
            var hasSevenPlus = cmcBuckets.Contains(7);
            var exactBuckets = cmcBuckets.Where(b => b < 7).ToHashSet();
            stream = stream.Where(c =>
                c.Cmc.HasValue
                && (exactBuckets.Contains(c.Cmc.Value)
                    || (hasSevenPlus && c.Cmc.Value >= 7)));
        }

        return stream.Take(limit).ToList();
    }

    private static bool MatchesColors(CardEntity c, HashSet<string> filter)
    {
        List<string>? cardColors;
        try
        {
            cardColors = JsonSerializer.Deserialize<List<string>>(c.Colors);
        }
        catch
        {
            cardColors = null;
        }
        cardColors ??= new List<string>();

        if (filter.Contains("C") && cardColors.Count == 0) return true;
        return cardColors.Any(cc => filter.Contains(cc, StringComparer.OrdinalIgnoreCase));
    }

    private static bool MatchesTypes(CardEntity c, HashSet<string> filter)
    {
        var typeLine = c.TypeLine ?? "";
        var typePart = typeLine.Split(" — ")[0];
        var tokens = typePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(t => filter.Contains(t));
    }

    public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names)
    {
        if (names == null) return Array.Empty<CardEntity>();
        var dict = _byName.Value;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<CardEntity>();
        foreach (var raw in names)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var lookup = CardNameAliases.Resolve(raw);
            if (!seen.Add(lookup)) continue;
            if (dict.TryGetValue(lookup, out var hit)) rows.Add(hit);
        }
        return rows;
    }

    public bool IsImplemented(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return _byName.Value.TryGetValue(name, out var hit) && hit.IsImplemented;
    }

    public void SetImplemented(string name, bool value)
    {
        // The implemented flag is derived from the [CardName] factory
        // registry at load time, not stored mutable state. Add (or remove)
        // a factory to change it. Throwing here surfaces the mistake
        // immediately instead of silently dropping the write (which the
        // old DbCardRepository would have persisted).
        _log.Warn(
            "EmbeddedCardRepository.SetImplemented({Name}, {Value}) is a no-op; " +
            "the implemented flag is derived from the [CardName] factory " +
            "registry. Add or remove a factory to change it.", name, value);
        throw new NotSupportedException(
            "EmbeddedCardRepository is read-only. IsImplemented is derived " +
            "from the [CardName] factory registry; add or remove a factory " +
            "to change it.");
    }

    public BotIntent IntentFor(string cardName) => BotIntent.None;

    /// <summary>For diagnostics / tests: the number of distinct cards
    /// loaded from the embedded seed.</summary>
    public int Count => _byName.Value.Count;

    // ----- loader plumbing -----

    private static IReadOnlyList<CardEntity> LoadFromEmbeddedResource()
    {
        var asm = typeof(EmbeddedCardRepository).Assembly;
        using var stream = asm.GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded card resource '{DefaultResourceName}' not found " +
                $"in assembly {asm.GetName().Name}. Regenerate the seed and " +
                $"ensure the EmbeddedResource <Include> in Majik.Core.csproj " +
                $"still matches the file path.");
        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        var rows = JsonSerializer.Deserialize<List<EmbeddedRow>>(gz, SerializerOptions)
            ?? throw new InvalidOperationException(
                "Embedded card seed deserialized to null.");
        var entities = new List<CardEntity>(rows.Count);
        foreach (var r in rows)
        {
            entities.Add(DeriveImplemented(r.ToEntity()));
        }
        return entities;
    }

    /// <summary>Overrides <paramref name="entity"/>'s <c>IsImplemented</c>
    /// with the load-time-derived value from the <c>[CardName]</c> factory
    /// registry, ignoring whatever flag was stored in the gzipped seed.
    /// This is what lets a card PR add a factory without regenerating
    /// <c>modern-cards.json.gz</c> — the binary seed was otherwise the
    /// source of a perpetual merge-conflict treadmill. The stored flag is
    /// kept in the file for human inspection only.
    ///
    /// For double-faced, adventure, and split cards (CR 712) the seed stores
    /// the full composite name <c>"Front // Back"</c>, but factories register
    /// only the front-face name via <c>[CardName("Front")]</c>. A secondary
    /// front-face check (<see cref="FrontFaceImplemented"/>) ensures those
    /// cards are not incorrectly flagged <c>IsImplemented=false</c>.</summary>
    internal static CardEntity DeriveImplemented(CardEntity entity)
    {
        entity.IsImplemented =
            Factories.ImplementedCardNames.Contains(entity.Name)
            || FrontFaceImplemented(entity.Name);
        return entity;
    }

    // CR 712 — double-faced/split/adventure cards are seeded as "Front // Back";
    // factories register the front-face name via [CardName], so a card counts
    // as implemented when its front face is in the registry.
    private static bool FrontFaceImplemented(string name)
    {
        var idx = name.IndexOf(" // ", StringComparison.Ordinal);
        if (idx < 0) return false;
        var front = name[..idx];
        return Factories.ImplementedCardNames.Contains(front);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Wire shape for one row in <c>modern-cards.json.gz</c>.
    /// Stays close to the export script's column set so the file can be
    /// regenerated by a follow-up PR without ceremony.</summary>
    private sealed record EmbeddedRow(
        string Name,
        string? ManaCost,
        string? TypeLine,
        string? OracleText,
        string? Power,
        string? Toughness,
        int? Loyalty,
        string? Colors,
        string? ColorIdentity,
        int? Cmc,
        [property: JsonConverter(typeof(IntToBoolConverter))]
        bool IsImplemented,
        string? ScryfallId)
    {
        public CardEntity ToEntity() => new()
        {
            ScryfallId = ScryfallId ?? "",
            Name = Name,
            ManaCost = string.IsNullOrEmpty(ManaCost) ? null : ManaCost,
            Cmc = Cmc,
            TypeLine = TypeLine ?? "",
            OracleText = OracleText,
            Power = Power,
            Toughness = Toughness,
            Loyalty = Loyalty,
            Colors = string.IsNullOrEmpty(Colors) ? "[]" : Colors,
            ColorIdentity = string.IsNullOrEmpty(ColorIdentity) ? "[]" : ColorIdentity,
            IsImplemented = IsImplemented,
        };
    }

    /// <summary>SQLite's <c>.mode json</c> emits booleans as 0/1
    /// integers; this converter accepts either form so the seed stays
    /// regenerable from raw sqlite output without a post-processing step.</summary>
    private sealed class IntToBoolConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Number => reader.GetInt32() != 0,
                JsonTokenType.String => bool.TryParse(reader.GetString(), out var b) && b,
                _ => false,
            };

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            => writer.WriteBooleanValue(value);
    }

    /// <summary>Minimal log seam — avoids a hard dependency on
    /// <c>Microsoft.Extensions.Logging</c> from <c>Majik.Core</c>.</summary>
    internal interface ILogSink
    {
        void Warn(string format, params object?[] args);
    }

    private sealed class NullLog : ILogSink
    {
        public static readonly NullLog Instance = new();
        public void Warn(string format, params object?[] args) { }
    }
}
