using Majik.Core.CardData.Database;

namespace Majik.Core.CardData;

/// <summary>
/// Read-side abstraction over the <c>CompiledSpellTemplates</c> table.
/// Returns the offline-compiled template match for a given card name —
/// the runtime fast path in <see cref="OracleSpellBinder"/>'s compiled
/// binder consults this before falling back to a live registry walk.
///
/// Production: <see cref="DbCompiledSpellTemplateRepository"/> against
/// the SQLite DB. Tests: in-memory implementation.
/// </summary>
public interface ICompiledSpellTemplateRepository
{
    /// <summary>Look up the compiled match for a card. Returns
    /// <c>null</c> when no row exists (unimported card, template
    /// doesn't match this card, or table not yet populated).</summary>
    CompiledSpellTemplateEntity? Lookup(string cardName);
}
