using System.Collections.Concurrent;
using System.Reflection;

namespace Majik.Bot.Strategies;

/// <summary>
/// Resolves a deck's <see cref="IDeckStrategy"/> by archetype name via a one-time
/// reflection scan for [DeckStrategy]-attributed types. Strategies MUST be stateless
/// (one cached instance is reused across games). Unknown name → null (unchanged behavior).
/// </summary>
internal static class DeckStrategyRegistry
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, IDeckStrategy>> _cache = new();

    public static IDeckStrategy? For(string archetypeName, Assembly? scan = null)
    {
        var asm = scan ?? typeof(IDeckStrategy).Assembly;
        return _cache.GetOrAdd(asm, Build).TryGetValue(archetypeName, out var s) ? s : null;
    }

    private static IReadOnlyDictionary<string, IDeckStrategy> Build(Assembly asm)
    {
        var map = new Dictionary<string, IDeckStrategy>();
        foreach (var t in asm.GetTypes())
        {
            if (t.IsAbstract || !typeof(IDeckStrategy).IsAssignableFrom(t)) continue;
            var attrs = t.GetCustomAttributes<DeckStrategyAttribute>().ToList();
            if (attrs.Count == 0) continue;
            // One instance shared across every archetype key the class declares
            // ([DeckStrategy] is AllowMultiple — e.g. BelcherComboSolver serves
            // both "AzoriusLotusBelcher" and "Belcher").
            var instance = (IDeckStrategy)Activator.CreateInstance(t)!;
            foreach (var attr in attrs)
                map[attr.DeckName] = instance;
        }
        return map;
    }
}
