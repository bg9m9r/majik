using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.Factories;
using Majik.Core.Players;

namespace Majik.Core.CardData.Coverage;

/// <summary>
/// Classifies a single <see cref="CardEntity"/> into a
/// <see cref="CoverageTier"/> using the production
/// <see cref="ScryfallCardFactory"/> pipeline. Pure: no I/O, deterministic.
///
/// Classification priority (first match wins):
/// <list type="number">
///   <item><see cref="CoverageTier.NamedFactory"/> — name is in the
///   <c>[CardName]</c> dispatch set discovered via reflection.</item>
///   <item><see cref="CoverageTier.SpellBound"/> — instant/sorcery whose
///   <see cref="ScryfallCardFactory.LookupSpellDefinition"/> returns
///   non-null against a stub caster/resolver.</item>
///   <item><see cref="CoverageTier.KeywordOnly"/> — permanent built by
///   the factory carries ≥1 ability and the oracle text consists of
///   nothing but keyword markers and reminder text.</item>
///   <item><see cref="CoverageTier.Vanilla"/> — creature with no oracle
///   text at all (and the factory attached no abilities).</item>
///   <item><see cref="CoverageTier.Unimplemented"/> — fallback. Card has
///   text but no factory, template, or keyword coverage.</item>
/// </list>
/// </summary>
public sealed class CoverageClassifier
{
    private readonly ScryfallCardFactory _factory;
    private readonly Player _stubCaster;
    private readonly IReadOnlySet<string> _namedFactoryNames;

    /// <summary>
    /// The full set of card names served by the
    /// <c>NamedCardFactory.CreateGenerated</c> dispatch table, sourced
    /// from every <see cref="CardNameAttribute"/> in the loaded
    /// assemblies. Exposed for diagnostics + tests.
    /// </summary>
    public IReadOnlySet<string> NamedFactoryNames => _namedFactoryNames;

    public CoverageClassifier(ScryfallCardFactory factory, Player stubCaster)
        : this(factory, stubCaster, DiscoverNamedFactoryNames())
    {
    }

    /// <summary>Test seam — inject the name set instead of reflecting.</summary>
    public CoverageClassifier(
        ScryfallCardFactory factory,
        Player stubCaster,
        IReadOnlySet<string> namedFactoryNames)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _stubCaster = stubCaster ?? throw new ArgumentNullException(nameof(stubCaster));
        _namedFactoryNames = namedFactoryNames ?? throw new ArgumentNullException(nameof(namedFactoryNames));
    }

    /// <summary>Classify a single card row.</summary>
    public CoverageTier Classify(CardEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (_namedFactoryNames.Contains(entity.Name))
        {
            return CoverageTier.NamedFactory;
        }

        var typeLine = entity.TypeLine ?? "";
        var isInstantOrSorcery =
            typeLine.Contains("Instant", StringComparison.OrdinalIgnoreCase) ||
            typeLine.Contains("Sorcery", StringComparison.OrdinalIgnoreCase);

        if (isInstantOrSorcery)
        {
            // Stub target-resolver — coverage only cares whether a
            // SpellDefinition exists, not whether it can resolve.
            var spell = SafeLookupSpell(entity.Name);
            if (spell is not null) return CoverageTier.SpellBound;
        }

        // For permanents (or instants/sorceries that failed SpellBound),
        // walk the binder chain via Create and inspect the result.
        var card = SafeCreate(entity.Name);
        var abilityCount = card?.Abilities.Count ?? 0;
        var hasOracleText = !string.IsNullOrWhiteSpace(entity.OracleText);
        var keywordOnly = !hasOracleText || IsKeywordOnlyOracleText(entity);

        var isCreature = typeLine.Contains("Creature", StringComparison.OrdinalIgnoreCase);

        if (!isInstantOrSorcery && abilityCount > 0 && keywordOnly)
        {
            return CoverageTier.KeywordOnly;
        }

        if (isCreature && !hasOracleText)
        {
            return CoverageTier.Vanilla;
        }

        return CoverageTier.Unimplemented;
    }

    private Majik.Core.Game.SpellDefinition? SafeLookupSpell(string name)
    {
        try
        {
            return _factory.LookupSpellDefinition(name, _stubCaster, o => o, stack: null);
        }
        catch
        {
            // Defensive: a buggy template should not crash a 30k-row sweep.
            return null;
        }
    }

    private Majik.Core.Cards.ICard? SafeCreate(string name)
    {
        try
        {
            return _factory.Create(name, _stubCaster);
        }
        catch
        {
            return null;
        }
    }

    private static readonly Regex ReminderTextRx = new(@"\([^)]*\)", RegexOptions.Compiled);

    /// <summary>
    /// True when every non-blank line of the oracle text — after stripping
    /// parenthesized reminder text — names one or more keywords from the
    /// card's Scryfall <c>Keywords</c> JSON array. This is the
    /// "vanilla + keyword" shape: e.g. "Flying", "Flying, vigilance",
    /// "Flying (This creature can't be blocked except by …)".
    /// </summary>
    internal static bool IsKeywordOnlyOracleText(CardEntity entity)
    {
        var raw = entity.OracleText ?? "";
        if (string.IsNullOrWhiteSpace(raw)) return true;

        var keywords = ParseKeywordsJson(entity.Keywords);
        if (keywords.Count == 0) return false;

        // Strip reminder text in parens, then split by lines + commas.
        var stripped = ReminderTextRx.Replace(raw, "");
        foreach (var line in stripped.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var token in line.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = token.Trim().TrimEnd('.').Trim();
                if (trimmed.Length == 0) continue;
                if (!keywords.Contains(trimmed)) return false;
            }
        }
        return true;
    }

    private static IReadOnlySet<string> ParseKeywordsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            return new HashSet<string>(arr, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Reflects every loaded assembly for classes carrying
    /// <see cref="CardNameAttribute"/> and returns the union of their
    /// <c>Name</c> values. Mirrors what the source generator scans at
    /// compile time, so any drift between the generator and runtime
    /// surface here.
    /// </summary>
    public static IReadOnlySet<string> DiscoverNamedFactoryNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        // Walk every loaded assembly; the source generator runs against
        // the same compilation. Skip dynamic / reflection-only assemblies
        // defensively — they don't carry [CardName] attributes anyway.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }
            foreach (var t in types)
            {
                foreach (var attr in t.GetCustomAttributes<CardNameAttribute>(inherit: false))
                {
                    if (!string.IsNullOrWhiteSpace(attr.Name)) set.Add(attr.Name);
                }
            }
        }

        // Defensive nudge: make sure Majik.Core is loaded so its factory
        // classes are reachable when this runs from a host that hasn't
        // touched the engine yet.
        _ = typeof(ScryfallCardFactory).Assembly;
        return set;
    }
}
