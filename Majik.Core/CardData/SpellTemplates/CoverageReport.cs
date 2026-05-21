using Majik.Core.CardData.Database;
using Majik.Core.Players;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Snapshot of which oracle templates currently match a card pool.
/// Built by <see cref="Build"/>; consumed by the
/// <c>coverage-report</c> console command. Pure data — no I/O, no
/// engine state mutation.
/// </summary>
public sealed record CoverageReport(
    int Total,
    int Matched,
    IReadOnlyDictionary<string, int> PerTemplateHits,
    IReadOnlyList<string> UnmatchedNames)
{
    /// <summary>
    /// Walks every entity, filters to instants/sorceries, runs the
    /// registry against each, and tallies the result. Uses
    /// <paramref name="synthCaster"/> as the spell caster in the
    /// SpellBindContext — templates only need it to construct the
    /// returned SpellDefinition; we discard the SpellDefinition
    /// since coverage only cares whether a match exists.
    /// </summary>
    public static CoverageReport Build(
        IEnumerable<CardEntity> entities,
        SpellTemplateRegistry registry,
        Player synthCaster)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(synthCaster);

        var perTemplate = new Dictionary<string, int>(StringComparer.Ordinal);
        var unmatched = new List<string>();
        int total = 0, matched = 0;

        foreach (var entity in entities.Where(IsInstantOrSorcery))
        {
            total++;
            var ctx = new SpellBindContext(entity, synthCaster, _ => _, null, null);
            var hitTemplate = registry.OrderedTemplates
                .FirstOrDefault(t => t.TryBind(ctx) is not null);

            if (hitTemplate is null)
            {
                unmatched.Add(entity.Name);
            }
            else
            {
                matched++;
                perTemplate[hitTemplate.Name] = perTemplate.GetValueOrDefault(hitTemplate.Name) + 1;
            }
        }

        unmatched.Sort(StringComparer.Ordinal);
        return new CoverageReport(total, matched, perTemplate, unmatched);
    }

    private static bool IsInstantOrSorcery(CardEntity entity) =>
        (entity.TypeLine?.Contains("Instant", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (entity.TypeLine?.Contains("Sorcery", StringComparison.OrdinalIgnoreCase) ?? false);
}
