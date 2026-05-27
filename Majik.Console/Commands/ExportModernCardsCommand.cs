using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;

namespace Majik.Console.Commands;

/// <summary>
/// Streams the Scryfall <c>all-cards.json</c> bulk export, filters down to
/// Modern-legal printings, dedupes by card name, marks the
/// engine-implemented subset via the <c>[CardName]</c> source-gen registry,
/// and writes the canonical <c>Majik.Core/CardData/Embedded/modern-cards.json.gz</c>
/// seed file consumed by <see cref="EmbeddedCardRepository"/>.
///
/// Replaces the one-shot SQLite-dumped seed shipped in PR #511 with a
/// repeatable CLI step so updating the card pool is just:
/// <c>dotnet run --project Majik.Console -- export-modern-cards &lt;path&gt;</c>
/// followed by committing the regenerated <c>.gz</c>.
///
/// ## Dedupe rule
///
/// One row per distinct card name. When Scryfall ships multiple printings
/// for the same name, the printing with the highest <c>released_at</c>
/// (lexicographic ISO date) wins; ties fall back to first-seen order.
/// Rationale: most-recent reprint usually carries the cleanest oracle text
/// (errata + templating updates). Documented here + in the CLI help so the
/// next regeneration matches.
///
/// ## What gets dropped
///
/// <list type="bullet">
/// <item><c>legalities.modern</c> not in <c>{legal, restricted}</c>
/// (so <c>banned</c> and <c>not_legal</c> are both filtered out).</item>
/// <item>Tokens / emblems / cards without a printed <c>name</c>.</item>
/// </list>
///
/// Set-level metadata (<c>set</c>, <c>collector_number</c>, etc.) is not
/// projected — the embedded seed is keyed by name and gameplay never
/// looks at the originating printing.
/// </summary>
public static class ExportModernCardsCommand
{
    /// <summary>Default output path relative to the repo root — matches the
    /// EmbeddedResource <c>Include</c> in <c>Majik.Core.csproj</c>.</summary>
    public const string DefaultOutputPath =
        "Majik.Core/CardData/Embedded/modern-cards.json.gz";

    /// <summary>Cards the verification step asserts a fragment of oracle
    /// text for. Defaults guard the canonical Modern pool — overridable
    /// for unit tests that drive the exporter with a synthetic input
    /// missing some of these names.</summary>
    internal static readonly IReadOnlyList<(string Name, string MustContain)>
        DefaultCanonicalSanityChecks = new (string, string)[]
        {
            ("Lightning Bolt", "3 damage"),
            ("Forest",         "{G}"),
            ("Mountain",       "{R}"),
        };

    public static Task<int> RunAsync(
        string scryfallBulkPath,
        string? outputPath = null,
        TextWriter? log = null)
        => RunAsync(scryfallBulkPath, outputPath, log, DefaultCanonicalSanityChecks);

    internal static async Task<int> RunAsync(
        string scryfallBulkPath,
        string? outputPath,
        TextWriter? log,
        IReadOnlyList<(string Name, string MustContain)> canonicalSanityChecks)
    {
        log ??= System.Console.Out;
        outputPath ??= DefaultOutputPath;

        if (!File.Exists(scryfallBulkPath))
        {
            log.WriteLine($"error: input not found: {scryfallBulkPath}");
            return 1;
        }

        long inputBytes = new FileInfo(scryfallBulkPath).Length;
        log.WriteLine(
            $"reading {scryfallBulkPath} ({inputBytes / (1024 * 1024)} MB)...");

        var implementedNames = LoadImplementedNames();
        log.WriteLine($"  {implementedNames.Count} implemented names from [CardName] registry");

        var stats = new ExportStats();
        var byName = new Dictionary<string, ExportRow>(StringComparer.Ordinal);

        await using (var fs = File.OpenRead(scryfallBulkPath))
        {
            foreach (var row in StreamModernRows(fs, stats))
            {
                stats.ModernKept++;
                if (byName.TryGetValue(row.Name, out var existing))
                {
                    if (PrefersReplacement(existing.ReleasedAt, row.ReleasedAt))
                    {
                        byName[row.Name] = row;
                        stats.DuplicateReplacements++;
                    }
                    else
                    {
                        stats.DuplicateSkipped++;
                    }
                }
                else
                {
                    byName[row.Name] = row;
                }
            }
        }

        log.WriteLine($"  {stats.TotalSeen} cards seen, {stats.ModernKept} Modern-legal kept");
        log.WriteLine($"  {stats.DuplicateReplacements} dedupe replacements, {stats.DuplicateSkipped} dedupe skips");
        log.WriteLine($"  {byName.Count} distinct names retained");

        int implementedCount = 0;
        var emitted = new List<EmbeddedRow>(byName.Count);
        foreach (var row in byName.Values.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            bool isImpl = implementedNames.Contains(row.Name);
            if (isImpl) implementedCount++;
            emitted.Add(row.ToEmbeddedRow(isImpl));
        }
        log.WriteLine($"  {implementedCount} marked isImplemented = true");

        var outDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        long preGzipBytes;
        using (var raw = new MemoryStream())
        {
            await JsonSerializer.SerializeAsync(raw, emitted, WriteOptions);
            preGzipBytes = raw.Length;

            raw.Position = 0;
            await using var outStream = File.Create(outputPath);
            await using var gz = new GZipStream(outStream, CompressionLevel.SmallestSize);
            await raw.CopyToAsync(gz);
        }

        long postGzipBytes = new FileInfo(outputPath).Length;
        log.WriteLine(
            $"wrote {outputPath} — {preGzipBytes:N0} B JSON → {postGzipBytes:N0} B gzipped " +
            $"({(double)postGzipBytes / preGzipBytes:P1} ratio)");

        // ---------- verification ----------
        log.WriteLine("verifying round-trip via EmbeddedCardRepository...");
        var repo = LoadRepoFromFile(outputPath);
        var problems = new List<string>();

        if (repo.Count != byName.Count)
            problems.Add($"repo.Count={repo.Count}, expected {byName.Count}");

        int reloadedImpl = 0;
        foreach (var name in implementedNames)
            if (repo.IsImplemented(name)) reloadedImpl++;
        if (reloadedImpl != implementedCount)
            problems.Add(
                $"implemented reload mismatch: {reloadedImpl} flagged, " +
                $"expected {implementedCount}");

        foreach (var (name, mustContain) in canonicalSanityChecks)
        {
            var hit = repo.GetByName(name);
            if (hit == null)
            {
                problems.Add($"{name}: missing from round-trip");
                continue;
            }
            if (hit.OracleText == null
                || !hit.OracleText.Contains(mustContain, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{name}: oracle text missing expected fragment '{mustContain}' " +
                    $"(got: {Truncate(hit.OracleText, 80)})");
            }
        }

        if (problems.Count > 0)
        {
            log.WriteLine("verification FAILED:");
            foreach (var p in problems) log.WriteLine("  - " + p);
            return 1;
        }

        log.WriteLine("verification OK.");
        return 0;
    }

    // ----- streaming -----

    /// <summary>Walk the top-level Scryfall bulk array one card at a time
    /// using <see cref="Utf8JsonReader"/> over a refillable buffer so peak
    /// memory stays bounded (~few MB) even for the full ~500 MB bulk file.
    /// Each card object is materialized into a <see cref="JsonDocument"/>
    /// just long enough to project an <see cref="ExportRow"/> from it.</summary>
    internal static IEnumerable<ExportRow> StreamModernRows(
        Stream input, ExportStats stats)
    {
        var pump = new ScryfallObjectPump(input);
        while (pump.TryReadNextCardObject(out var cardJsonOwner))
        {
            using (cardJsonOwner)
            {
                stats.TotalSeen++;
                var row = ProjectIfModernLegal(cardJsonOwner.Document.RootElement);
                if (row != null) yield return row;
            }
        }
    }

    /// <summary>Stream-decoder for a Scryfall <c>all-cards.json</c> array.
    /// Skips the opening <c>[</c>, then on each call materializes the next
    /// element into a standalone <see cref="JsonDocument"/>. Buffer grows
    /// on demand for any card whose JSON exceeds the initial window.</summary>
    internal sealed class ScryfallObjectPump
    {
        internal const int DefaultInitialBufferSize = 64 * 1024;
        private readonly Stream _input;
        private byte[] _buffer;
        private int _bufferEnd;
        private bool _isEof;
        private bool _arrayStarted;
        private bool _arrayClosed;
        private JsonReaderState _state = new(new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        public ScryfallObjectPump(Stream input)
            : this(input, DefaultInitialBufferSize) { }

        /// <summary>Test hook: small initial buffer forces the
        /// boundary-crossing + grow paths on payloads the production
        /// 64 KB window would swallow whole.</summary>
        internal ScryfallObjectPump(Stream input, int initialBufferSize)
        {
            if (initialBufferSize < 16)
                throw new ArgumentOutOfRangeException(nameof(initialBufferSize),
                    "buffer must hold at least the opening '[' plus a small card prefix");
            _input = input;
            _buffer = new byte[initialBufferSize];
        }

        public bool TryReadNextCardObject(out CardDocumentHandle handle)
        {
            handle = default!;
            if (_arrayClosed) return false;

            while (true)
            {
                if (!_arrayStarted)
                {
                    EnsureBufferLoaded();
                    var openReader = new Utf8JsonReader(
                        _buffer.AsSpan(0, _bufferEnd), _isEof, _state);
                    if (!openReader.Read())
                    {
                        if (_isEof)
                            throw new InvalidOperationException("empty Scryfall input");
                        GrowOrAdvance(0);
                        continue;
                    }
                    if (openReader.TokenType != JsonTokenType.StartArray)
                        throw new InvalidOperationException(
                            "Scryfall bulk input is not a JSON array at the root.");
                    Advance(openReader.BytesConsumed, openReader.CurrentState);
                    _arrayStarted = true;
                }

                EnsureBufferLoaded();
                var reader = new Utf8JsonReader(
                    _buffer.AsSpan(0, _bufferEnd), _isEof, _state);
                if (!reader.Read())
                {
                    if (_isEof) { _arrayClosed = true; return false; }
                    GrowOrAdvance(0);
                    continue;
                }

                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    Advance(reader.BytesConsumed, reader.CurrentState);
                    _arrayClosed = true;
                    return false;
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    // Defensive: ignore any unexpected token (commas are
                    // already absorbed by the reader between elements).
                    Advance(reader.BytesConsumed, reader.CurrentState);
                    continue;
                }

                // We're sitting on a card-object start. Walk balanced
                // braces to find its end within the current buffer. If
                // not present, refill / grow and try again — _state is
                // intentionally not advanced so the next iteration sees
                // the same StartObject.
                long startOffset = reader.TokenStartIndex;
                int depth = 1;
                long endExclusive = -1;
                while (depth > 0 && reader.Read())
                {
                    switch (reader.TokenType)
                    {
                        case JsonTokenType.StartObject:
                        case JsonTokenType.StartArray:
                            depth++;
                            break;
                        case JsonTokenType.EndObject:
                        case JsonTokenType.EndArray:
                            depth--;
                            if (depth == 0) endExclusive = reader.BytesConsumed;
                            break;
                    }
                }

                if (endExclusive < 0)
                {
                    if (_isEof)
                        throw new InvalidOperationException(
                            "Scryfall bulk input ended mid-card object.");
                    // Preserve the entire partial-object prefix (including
                    // any inter-element whitespace / comma before the
                    // StartObject at startOffset) so the saved _state — which
                    // still reflects "between array elements" — stays aligned
                    // with buffer[0]. Dropping bytes before startOffset would
                    // skip the comma the reader's state machine expects,
                    // surfacing later as "'{' is invalid after a value".
                    GrowOrAdvance(0);
                    continue;
                }

                // Materialize a private copy of just this object's bytes.
                var slice = _buffer.AsSpan(
                    (int)startOffset, (int)(endExclusive - startOffset)).ToArray();
                var doc = JsonDocument.Parse(slice);
                handle = new CardDocumentHandle(doc);
                Advance(endExclusive, reader.CurrentState);
                return true;
            }
        }

        private void EnsureBufferLoaded()
        {
            if (_isEof || _bufferEnd > 0) return;
            int read = _input.Read(_buffer, 0, _buffer.Length);
            _bufferEnd = read;
            if (read == 0) _isEof = true;
        }

        private void Advance(long consumed, JsonReaderState newState)
        {
            int c = (int)consumed;
            int remaining = _bufferEnd - c;
            if (remaining > 0)
                Buffer.BlockCopy(_buffer, c, _buffer, 0, remaining);
            _bufferEnd = remaining;
            _state = newState;
            if (!_isEof) RefillTail();
        }

        /// <summary>Called when the reader couldn't finish a token within
        /// the current buffer. Compacts unread bytes to the front (starting
        /// at <paramref name="keepFromOffset"/>) and either reads more from
        /// the stream or, if the buffer is already full of one unfinished
        /// object, grows it.</summary>
        private void GrowOrAdvance(int keepFromOffset)
        {
            if (keepFromOffset > 0)
            {
                int remaining = _bufferEnd - keepFromOffset;
                Buffer.BlockCopy(_buffer, keepFromOffset, _buffer, 0, remaining);
                _bufferEnd = remaining;
            }

            if (_isEof) return;

            if (_bufferEnd == _buffer.Length)
            {
                // One card is larger than the buffer — grow it.
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }
            RefillTail();
        }

        private void RefillTail()
        {
            int space = _buffer.Length - _bufferEnd;
            if (space == 0) return;
            int read = _input.Read(_buffer, _bufferEnd, space);
            if (read == 0) _isEof = true;
            else _bufferEnd += read;
        }
    }

    /// <summary>Owned <see cref="JsonDocument"/> handle so callers can
    /// dispose deterministically. Disposing releases the document's
    /// pooled buffers.</summary>
    internal readonly struct CardDocumentHandle : IDisposable
    {
        public JsonDocument Document { get; }
        public CardDocumentHandle(JsonDocument doc) { Document = doc; }
        public void Dispose() => Document?.Dispose();
    }

    /// <summary>Test entry point: streams every card object out of
    /// <paramref name="input"/> using an arbitrarily small buffer so the
    /// grow + boundary-crossing branches in <see cref="ScryfallObjectPump"/>
    /// are exercised. Returns the raw card JSON strings — name extraction
    /// is delegated to the caller so the assertion stays close to the
    /// actual bytes that crossed the boundary.</summary>
    internal static List<string> StreamCardJsonForTesting(
        Stream input, int initialBufferSize)
    {
        var pump = new ScryfallObjectPump(input, initialBufferSize);
        var results = new List<string>();
        while (pump.TryReadNextCardObject(out var handle))
        {
            using (handle)
            {
                results.Add(handle.Document.RootElement.GetRawText());
            }
        }
        return results;
    }

    internal static ExportRow? ProjectIfModernLegal(JsonElement card)
    {
        if (!card.TryGetProperty("name", out var nameEl)
            || nameEl.ValueKind != JsonValueKind.String) return null;
        var name = nameEl.GetString();
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (!card.TryGetProperty("legalities", out var legalities)
            || legalities.ValueKind != JsonValueKind.Object) return null;
        if (!legalities.TryGetProperty("modern", out var modernEl)
            || modernEl.ValueKind != JsonValueKind.String) return null;
        var modern = modernEl.GetString();
        if (modern != "legal" && modern != "restricted") return null;

        return new ExportRow(
            Name: name,
            ManaCost: TryGetString(card, "mana_cost"),
            TypeLine: TryGetString(card, "type_line"),
            OracleText: TryGetString(card, "oracle_text"),
            Power: TryGetString(card, "power"),
            Toughness: TryGetString(card, "toughness"),
            Loyalty: TryGetIntFromMaybeString(card, "loyalty"),
            Colors: TryGetStringArrayAsJson(card, "colors"),
            ColorIdentity: TryGetStringArrayAsJson(card, "color_identity"),
            Cmc: TryGetIntFromNumber(card, "cmc"),
            ScryfallId: TryGetString(card, "id"),
            ReleasedAt: TryGetString(card, "released_at") ?? "");
    }

    private static string? TryGetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? TryGetIntFromNumber(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt32(out var n)
                ? n
                : (int?)Math.Round(v.GetDouble()),
            JsonValueKind.String when int.TryParse(
                v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }

    private static int? TryGetIntFromMaybeString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(
                v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };
    }

    private static string? TryGetStringArrayAsJson(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)
            || v.ValueKind != JsonValueKind.Array) return null;
        var items = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                items.Add(item.GetString()!);
        return JsonSerializer.Serialize(items);
    }

    /// <summary>Newer release date wins. Empty / missing dates lose to any
    /// dated row; otherwise lexicographic ISO compare is correct.</summary>
    internal static bool PrefersReplacement(string existing, string candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        if (string.IsNullOrEmpty(existing)) return true;
        return string.CompareOrdinal(candidate, existing) > 0;
    }

    // ----- implemented-name registry -----

    /// <summary>The set of printed names backed by a <c>[CardName]</c>
    /// factory (plus the inline basic-land + vanilla fallbacks). Delegates
    /// to <see cref="ImplementedCardNames"/> in <c>Majik.Core</c> — the
    /// single source of truth shared with the runtime
    /// <see cref="EmbeddedCardRepository"/>, which derives the same flag at
    /// load time. The exported seed keeps a stored <c>isImplemented</c>
    /// column for human inspection, but it is no longer authoritative:
    /// adding a factory flips the runtime flag without regenerating the
    /// gzipped seed.</summary>
    internal static HashSet<string> LoadImplementedNames() =>
        ImplementedCardNames.All.ToHashSet(StringComparer.Ordinal);

    // ----- output -----

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        // Match the camelCase the embedded loader accepts (it is
        // case-insensitive, but emitting camelCase keeps the seed's wire
        // shape readable + close to Scryfall's own field names).
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static EmbeddedCardRepository LoadRepoFromFile(string gzipPath)
    {
        // Reuse EmbeddedCardRepository's internal loader-delegate ctor so
        // verification exercises the same deserialization path production
        // takes. Reflection here is the cheapest way to avoid widening the
        // public surface for a one-shot CLI verification step.
        var ctor = typeof(EmbeddedCardRepository).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            new[]
            {
                typeof(Func<IReadOnlyList<CardEntity>>),
                typeof(EmbeddedCardRepository).GetNestedType(
                    "ILogSink", BindingFlags.NonPublic)!,
            })
            ?? throw new InvalidOperationException(
                "EmbeddedCardRepository internal ctor signature changed; " +
                "update ExportModernCardsCommand.LoadRepoFromFile.");

        IReadOnlyList<CardEntity> Load()
        {
            using var fs = File.OpenRead(gzipPath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            var rows = JsonSerializer.Deserialize<List<EmbeddedRow>>(
                gz,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("seed deserialized to null");
            return rows.Select(r => r.ToEntity()).ToList();
        }

        return (EmbeddedCardRepository)ctor.Invoke(
            new object?[] { (Func<IReadOnlyList<CardEntity>>)Load, null });
    }

    private static string Truncate(string? s, int max)
    {
        if (s == null) return "<null>";
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    public static string HelpText =>
        """
        export-modern-cards <scryfall-all-cards.json> [output-path]

          Stream-parses the Scryfall bulk export, keeps printings with
          legalities.modern in {legal, restricted}, dedupes by card name
          (preferring the printing with the highest released_at), marks
          rows backed by a [CardName] factory as isImplemented = true,
          and writes Majik.Core/CardData/Embedded/modern-cards.json.gz
          (or the supplied output-path) in the schema consumed by
          EmbeddedCardRepository.

          After writing, the seed is round-tripped through
          EmbeddedCardRepository and sanity-checked (Lightning Bolt,
          Forest, Mountain present; implemented count matches). Exits 1
          on any verification failure.

          Workflow:
            curl -o /tmp/scryfall.json \
              $(curl -s https://api.scryfall.com/bulk-data \
                | jq -r '.data[] | select(.type=="all_cards") | .download_uri')
            dotnet run --project Majik.Console -- export-modern-cards /tmp/scryfall.json
            git add Majik.Core/CardData/Embedded/modern-cards.json.gz
        """;

    // ----- internal record shapes -----

    internal sealed record ExportRow(
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
        string? ScryfallId,
        string ReleasedAt)
    {
        public EmbeddedRow ToEmbeddedRow(bool isImplemented) => new(
            Name: Name,
            ManaCost: ManaCost,
            TypeLine: TypeLine,
            OracleText: OracleText,
            Power: Power,
            Toughness: Toughness,
            Loyalty: Loyalty,
            Colors: Colors,
            ColorIdentity: ColorIdentity,
            Cmc: Cmc,
            IsImplemented: isImplemented,
            ScryfallId: ScryfallId);
    }

    /// <summary>Wire shape mirroring <c>EmbeddedCardRepository.EmbeddedRow</c>
    /// (which is private). Field order + names must match the loader's
    /// case-insensitive deserialization there.</summary>
    internal sealed record EmbeddedRow(
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

    internal sealed class ExportStats
    {
        public int TotalSeen;
        public int ModernKept;
        public int DuplicateReplacements;
        public int DuplicateSkipped;
    }
}
