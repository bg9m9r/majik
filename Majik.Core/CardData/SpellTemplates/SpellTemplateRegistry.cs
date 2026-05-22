using Majik.Core.Game;

namespace Majik.Core.CardData.SpellTemplates;

/// <summary>
/// Picks the first ISpellTemplate (highest Priority first) that produces
/// a non-null SpellDefinition for the given context. Returns null when
/// no template matches.
/// </summary>
public sealed class SpellTemplateRegistry
{
    public IReadOnlyList<ISpellTemplate> OrderedTemplates { get; }

    public SpellTemplateRegistry(IEnumerable<ISpellTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        OrderedTemplates = templates
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    public SpellDefinition? TryBind(SpellBindContext ctx)
    {
        foreach (var t in OrderedTemplates)
        {
            if (t.TryBind(ctx) is { } def) return def.WithIntentStamp(t.Intent);
        }
        return null;
    }
}
