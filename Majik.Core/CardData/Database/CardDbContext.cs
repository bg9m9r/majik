using Microsoft.EntityFrameworkCore;

namespace Majik.Core.CardData.Database;

/// <summary>
/// EF Core DbContext for card database.
/// </summary>
public class CardDbContext : DbContext
{
    public CardDbContext() { }

    public CardDbContext(DbContextOptions<CardDbContext> options) : base(options) { }

    public DbSet<CardEntity> Cards { get; set; } = null!;
    public DbSet<CardAbilityEntity> CardAbilities { get; set; } = null!;
    public DbSet<EffectReferenceEntity> EffectReferences { get; set; } = null!;
    public DbSet<CardAbilityEffectEntity> CardAbilityEffects { get; set; } = null!;
    public DbSet<KeywordMetadataEntity> KeywordMetadata { get; set; } = null!;
    public DbSet<ClaudeRequestCacheEntity> ClaudeRequestCache { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dbPath = GetDatabasePath();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CardEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Indexes for fast lookups
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.TypeLine);
            entity.HasIndex(e => e.Set);
            entity.HasIndex(e => e.ScryfallId).IsUnique();
            entity.HasIndex(e => new { e.Set, e.CollectorNumber });
            
            // Column configurations
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(500);
            
            entity.Property(e => e.ScryfallId)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.TypeLine)
                .IsRequired()
                .HasMaxLength(500);
        });
        
        // Configure CardAbilityEntity
        modelBuilder.Entity<CardAbilityEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasIndex(e => e.CardId);
            entity.HasIndex(e => new { e.CardId, e.AbilityIndex });
            
            entity.Property(e => e.EffectReferences)
                .IsRequired()
                .HasDefaultValue("[]");
            
            entity.Property(e => e.ParsedAt)
                .IsRequired();
            
            // Relationship to CardEntity
            entity.HasOne(e => e.Card)
                .WithMany()
                .HasForeignKey(e => e.CardId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // Configure EffectReferenceEntity
        modelBuilder.Entity<EffectReferenceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasIndex(e => e.EffectId).IsUnique();
            entity.HasIndex(e => e.Type);
            
            entity.Property(e => e.EffectId)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);
            
            entity.Property(e => e.Parameters)
                .IsRequired()
                .HasDefaultValue("{}");
            
            entity.Property(e => e.CreatedAt)
                .IsRequired();
        });
        
        // Configure CardAbilityEffectEntity
        modelBuilder.Entity<CardAbilityEffectEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasIndex(e => e.CardAbilityId);
            entity.HasIndex(e => e.EffectReferenceId);
            entity.HasIndex(e => new { e.CardAbilityId, e.EffectOrder });
            
            entity.Property(e => e.EffectParameters)
                .IsRequired()
                .HasDefaultValue("{}");
            
            // Relationship to CardAbilityEntity
            entity.HasOne(e => e.CardAbility)
                .WithMany(a => a.Effects)
                .HasForeignKey(e => e.CardAbilityId)
                .OnDelete(DeleteBehavior.Cascade);
            
            // Relationship to EffectReferenceEntity
            entity.HasOne(e => e.EffectReference)
                .WithMany(r => r.CardAbilityEffects)
                .HasForeignKey(e => e.EffectReferenceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // Configure KeywordMetadataEntity
        modelBuilder.Entity<KeywordMetadataEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasIndex(e => e.Keyword).IsUnique();
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.ImplementationStatus);
            entity.HasIndex(e => e.BaseKeyword);
            
            entity.Property(e => e.Keyword)
                .IsRequired()
                .HasMaxLength(200);
            
            entity.Property(e => e.Confidence)
                .IsRequired();
            
            entity.Property(e => e.CreatedAt)
                .IsRequired();
            
            // Store Claude data as JSON/text (can be large)
            entity.Property(e => e.Notes)
                .HasColumnType("TEXT");
            
            entity.Property(e => e.CodeExample)
                .HasColumnType("TEXT");
            
            entity.Property(e => e.TestingNotes)
                .HasColumnType("TEXT");
            
            entity.Property(e => e.ClaudeRawResponse)
                .HasColumnType("TEXT");
        });
        
        // Configure ClaudeRequestCacheEntity
        modelBuilder.Entity<ClaudeRequestCacheEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            entity.HasIndex(e => e.RequestHash).IsUnique();
            entity.HasIndex(e => e.Keyword);
            entity.HasIndex(e => e.RequestedAt);
            
            entity.Property(e => e.RequestHash)
                .IsRequired()
                .HasMaxLength(64); // SHA256 hash is 64 hex characters
            
            entity.Property(e => e.Keyword)
                .IsRequired()
                .HasMaxLength(200);
            
            entity.Property(e => e.RequestPrompt)
                .IsRequired()
                .HasColumnType("TEXT");
            
            entity.Property(e => e.ResponseText)
                .IsRequired()
                .HasColumnType("TEXT");
            
            entity.Property(e => e.ParsedNotes)
                .HasColumnType("TEXT");
            
            entity.Property(e => e.Model)
                .IsRequired()
                .HasMaxLength(100);
            
            entity.Property(e => e.RequestedAt)
                .IsRequired();
            
            entity.Property(e => e.LastAccessedAt)
                .IsRequired();
        });
    }

    /// <summary>
    /// Gets the database file path.
    /// Stores in user's app data directory.
    /// </summary>
    private static string GetDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var majikDir = Path.Combine(appData, "Majik");
        Directory.CreateDirectory(majikDir);
        return Path.Combine(majikDir, "cards.db");
    }
}
