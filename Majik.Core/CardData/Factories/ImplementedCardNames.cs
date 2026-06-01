using System.Collections.Immutable;
using System.Reflection;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Single source of truth for the set of printed card names the engine
/// actually implements. A name counts as implemented when either:
///
/// <list type="number">
/// <item>a class in <c>Majik.Core</c> carries a <see cref="CardNameAttribute"/>
/// for it (the bulk of the dispatch table — these are wired in by
/// <c>Majik.Core.SourceGen.NamedCardFactoryGenerator</c>), or</item>
/// <item>it is one of the inline fallbacks hand-listed in
/// <see cref="NamedCardFactory.Create"/> (basic lands + a few vanilla
/// test creatures that build a runtime card directly rather than through
/// a <c>*Factory</c>).</item>
/// </list>
///
/// This set is computed once via reflection and cached. It is the
/// authority for <c>IsImplemented</c> at runtime: <see cref="EmbeddedCardRepository"/>
/// recomputes the flag from this set when it loads the embedded seed,
/// rather than trusting whatever was baked into <c>modern-cards.json.gz</c>.
/// The export tool (<c>ExportModernCardsCommand</c>) reuses the same set so
/// the committed seed's stored flag stays human-inspectable and in sync —
/// but the stored value is no longer load-bearing.
///
/// Decoupling the flag from the binary seed is deliberate: a card PR that
/// only adds a <c>[CardName]</c> factory no longer has to regenerate the
/// gzipped seed, which would otherwise make every such PR conflict with
/// every other one (the binary "conflict treadmill").
/// </summary>
public static class ImplementedCardNames
{
    /// <summary>Inline fallbacks in <see cref="NamedCardFactory.Create"/>
    /// (basic lands + a few vanilla creatures). These construct a runtime
    /// card directly instead of dispatching to a <c>*Factory</c>, so they
    /// are not discoverable by reflecting over <see cref="CardNameAttribute"/>.
    /// Keep in sync if that inline switch grows.</summary>
    public static readonly ImmutableArray<string> InlineFallbackNames =
        ImmutableArray.Create(
            "Mountain", "Forest", "Plains", "Island", "Swamp", "Wastes",
            "Grizzly Bears", "Runeclaw Bear", "Hill Giant", "Centaur Courser");

    private static readonly Lazy<ImmutableHashSet<string>> _all =
        new(Compute, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<ImmutableHashSet<string>> _factoryBacked =
        new(ComputeFactoryBacked, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The implemented-name set, computed once and cached.
    /// Case-sensitive (<see cref="StringComparer.Ordinal"/>) to match how
    /// the dispatch table and the seed's <c>Name</c> column are keyed.</summary>
    public static ImmutableHashSet<string> All => _all.Value;

    /// <summary>
    /// The subset of <see cref="All"/> that is backed by a real
    /// <c>[CardName]</c> <c>*Factory</c> class — i.e. <see cref="All"/> MINUS
    /// the <see cref="InlineFallbackNames"/> (basic lands + the four vanilla
    /// test creatures, which <c>NamedCardFactory.Create</c> builds inline
    /// without dispatching to a factory). These are the names whose factory
    /// can carry bespoke abilities the binder chain does not synthesize, so
    /// they are the candidates for production routing through their factory.
    /// </summary>
    public static ImmutableHashSet<string> FactoryBackedNames => _factoryBacked.Value;

    /// <summary>True when <paramref name="name"/> is backed by a
    /// <c>[CardName]</c> factory or an inline fallback.</summary>
    public static bool Contains(string name) =>
        !string.IsNullOrEmpty(name) && _all.Value.Contains(name);

    /// <summary>True when <paramref name="name"/> is backed by a real
    /// <c>[CardName]</c> <c>*Factory</c> class (i.e. it is in
    /// <see cref="FactoryBackedNames"/>, not merely an inline fallback).</summary>
    public static bool HasRealFactory(string name) =>
        !string.IsNullOrEmpty(name) && _factoryBacked.Value.Contains(name);

    private static ImmutableHashSet<string> ComputeFactoryBacked() =>
        _all.Value.Except(InlineFallbackNames);

    private static ImmutableHashSet<string> Compute()
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        var asm = typeof(CardNameAttribute).Assembly;
        foreach (var type in SafeGetTypes(asm))
        {
            foreach (var attr in type.GetCustomAttributes<CardNameAttribute>(
                inherit: false))
            {
                if (!string.IsNullOrWhiteSpace(attr.Name))
                    builder.Add(attr.Name);
            }
        }

        // Fileless JSON cards (PLAN 03 Slice 3) — names dispatched straight
        // from CardData/Cards/*.json with no [CardName] wrapper class. The
        // source generator emits them into NamedCardFactory.GeneratedJsonCardNames;
        // folding them in here keeps the implemented-name set unchanged when a
        // wrapper is deleted in favour of its generated arm.
        foreach (var jsonName in Majik.Core.CardData.NamedCardFactory.GeneratedJsonCardNames)
        {
            if (!string.IsNullOrWhiteSpace(jsonName))
                builder.Add(jsonName);
        }

        foreach (var inline in InlineFallbackNames) builder.Add(inline);
        return builder.ToImmutable();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null)!;
        }
    }
}
