namespace Majik.Core.CardData;

/// <summary>
/// Maps alternate printings/Secret-Lair renames to their canonical
/// implemented card name. Consulted by <see cref="DbCardRepository.GetByName"/>
/// when an exact-name lookup misses — the alias points back at the row
/// with the wired-up oracle text.
///
/// Add an entry when a Universes-Beyond / Secret-Lair reprint has a
/// different display name but is functionally identical to a card already
/// implemented by the engine. Both names then resolve to the same
/// CardEntity, so decklists work regardless of which printing was typed.
/// </summary>
public static class CardNameAliases
{
    private static readonly Dictionary<string, string> _map = new(StringComparer.Ordinal)
    {
        // Fallout Secret Lair reprint of Walking Ballista — same rules text,
        // different art + name. (Universes Beyond: Fallout, 2024.)
        ["Assaultron Invader"] = "Walking Ballista",
    };

    /// <summary>Returns the canonical card name for <paramref name="name"/>,
    /// or <paramref name="name"/> itself if no alias exists.</summary>
    public static string Resolve(string name)
        => _map.TryGetValue(name, out var canonical) ? canonical : name;

    /// <summary>True if <paramref name="name"/> has an alias entry.</summary>
    public static bool HasAlias(string name) => _map.ContainsKey(name);
}
