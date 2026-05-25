using System.Text.Json;
using Majik.Core.CardData;
using Majik.Core.Cards;

namespace Majik.Core.Tests.Snapshots;

/// <summary>
/// In-memory <see cref="ICardRepository"/> populated from JSON files under
/// <c>Snapshots/card-data/</c>. The point: the snapshot tests run in CI
/// without the user's local <c>cards.db</c>, so the inputs are bundled with
/// the tests.
///
/// Files are slug-named (<c>lightning-bolt.json</c>) and serialize the same
/// <see cref="CardEntity"/> shape the production EF repo returns.
/// </summary>
internal sealed class FixtureCardRepository : ICardRepository
{
    private readonly Dictionary<string, CardEntity> _byName;

    public FixtureCardRepository(IEnumerable<CardEntity> entities)
    {
        _byName = new Dictionary<string, CardEntity>(StringComparer.Ordinal);
        foreach (var e in entities)
        {
            if (!string.IsNullOrEmpty(e.Name))
            {
                _byName[e.Name] = e;
            }
        }
    }

    public static FixtureCardRepository LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return new FixtureCardRepository(Array.Empty<CardEntity>());
        }
        var entities = new List<CardEntity>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            using var stream = File.OpenRead(file);
            var entity = JsonSerializer.Deserialize<CardEntity>(stream);
            if (entity != null) entities.Add(entity);
        }
        return new FixtureCardRepository(entities);
    }

    public CardEntity? GetByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _byName.TryGetValue(name, out var e) ? e : null;
    }

    public IReadOnlyList<CardEntity> Search(
        string? q,
        bool implementedOnly,
        int limit,
        IReadOnlyList<string>? colors = null,
        IReadOnlyList<string>? types = null,
        IReadOnlyList<int>? cmcBuckets = null)
        => Array.Empty<CardEntity>();

    public IReadOnlyList<CardEntity> GetByNames(IEnumerable<string> names)
    {
        var result = new List<CardEntity>();
        foreach (var n in names)
        {
            if (_byName.TryGetValue(n, out var e)) result.Add(e);
        }
        return result;
    }

    public bool IsImplemented(string name) =>
        GetByName(name)?.IsImplemented ?? false;

    public void SetImplemented(string name, bool value)
    {
        throw new NotSupportedException("FixtureCardRepository is read-only.");
    }
}
