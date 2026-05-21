using Majik.Core.CardData.Database;
using Microsoft.EntityFrameworkCore;

namespace Majik.Core.CardData;

/// <summary>
/// EF Core implementation of
/// <see cref="ICompiledSpellTemplateRepository"/>. Reads the
/// <c>CompiledSpellTemplates</c> table; one row per distinct card name.
///
/// Thread-safety mirrors <see cref="DbCardRepository"/>: takes a context
/// factory and opens a fresh <see cref="CardDbContext"/> per call when
/// it owns the lifetime.
/// </summary>
public sealed class DbCompiledSpellTemplateRepository : ICompiledSpellTemplateRepository
{
    private readonly Func<CardDbContext> _contextFactory;
    private readonly bool _ownsContext;

    public DbCompiledSpellTemplateRepository(Func<CardDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _ownsContext = true;
    }

    /// <summary>Legacy constructor — takes a single shared context. Used
    /// by test fixtures that build their own context inline.</summary>
    public DbCompiledSpellTemplateRepository(CardDbContext db)
    {
        if (db == null) throw new ArgumentNullException(nameof(db));
        _contextFactory = () => db;
        _ownsContext = false;
    }

    public CompiledSpellTemplateEntity? Lookup(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return null;

        var db = _contextFactory();
        try
        {
            return db.CompiledSpellTemplates.AsNoTracking()
                .FirstOrDefault(c => c.CardName == cardName);
        }
        finally
        {
            if (_ownsContext) db.Dispose();
        }
    }
}
