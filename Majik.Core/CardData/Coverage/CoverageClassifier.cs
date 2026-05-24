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
///   <item>Land branch — Basic Land or shock-land-clause match → engine
///   covers the printed rules text via inherent mana abilities and the
///   <see cref="ShockLandBinder"/>, reported as
///   <see cref="CoverageTier.KeywordOnly"/>.</item>
///   <item><see cref="CoverageTier.SpellBound"/> — instant/sorcery whose
///   <see cref="ScryfallCardFactory.LookupSpellDefinition"/> returns
///   non-null against a stub caster/resolver.</item>
///   <item><see cref="CoverageTier.KeywordOnly"/> — non-instant/sorcery
///   built by the factory whose oracle text was fully captured by the
///   binder chain (signalled by <see cref="ICard.IsVanillaShell"/> being
///   <c>false</c> with at least one bound ability).</item>
///   <item><see cref="CoverageTier.Vanilla"/> — creature (or Wastes-like
///   land) with no printed oracle text and no factory abilities.</item>
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
        var isLand = typeLine.Contains("Land", StringComparison.OrdinalIgnoreCase);
        var isBasic = typeLine.Contains("Basic", StringComparison.OrdinalIgnoreCase);

        // Land branch — the classifier's keyword-only oracle check is
        // unreliable for lands because Scryfall leaves Keywords=[] on
        // basics + shocks. Resolve those two flavours up-front via
        // type-line + the shared shock-clause regex; everything else
        // falls through into the binder-driven path below.
        if (isLand && !isInstantOrSorcery)
        {
            // Basic Land — engine's BasicLandManaColors + the
            // OracleManaBinder fully cover the card's "({T}: Add {C}.)"
            // reminder text. Always reported as KeywordOnly.
            if (isBasic)
            {
                return CoverageTier.KeywordOnly;
            }

            // Shock-land cycle — covered by ShockLandBinder via oracle
            // regex even though the binder runs against a ReplacementBus
            // that isn't always wired during classification. We trust
            // the regex match alone here.
            var oracle = entity.OracleText ?? string.Empty;
            if (ShockLandBinder.ShockClause.IsMatch(oracle))
            {
                return CoverageTier.KeywordOnly;
            }
        }

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

        // Lands with at least one bound mana / activated / triggered
        // ability via the binder chain (e.g. dual-mana taplands, utility
        // lands with a tap-for-mana clause) count as keyword-only — the
        // engine knows how to play them even if the oracle text isn't
        // pure keyword markers.
        if (isLand && abilityCount > 0)
        {
            return CoverageTier.KeywordOnly;
        }

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

        // Land with no abilities and no oracle text (e.g. Wastes-style):
        // engine plays it as a colourless do-nothing tapland. Tier it as
        // Vanilla so it isn't penalised as Unimplemented in the report.
        if (isLand && !hasOracleText && abilityCount == 0)
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
    public static bool IsKeywordOnlyOracleText(CardEntity entity)
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
