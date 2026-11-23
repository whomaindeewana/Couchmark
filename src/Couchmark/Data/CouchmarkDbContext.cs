using Couchmark.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Couchmark.Data;

/// <summary>
/// The main Entity Framework Core database context for Couchmark.
/// Manages all database tables and their relationships.
/// The SQLite database file is stored at %AppData%\Couchmark\Couchmark.db.
/// </summary>
public class CouchmarkDbContext : DbContext
{
    /// <summary>Initialises the context with the provided options (injected via DI).</summary>
    public CouchmarkDbContext(DbContextOptions<CouchmarkDbContext> options) : base(options) { }

    // ── Tables (DbSets) ──────────────────────────────────────────────────────

    /// <summary>Movies in the local library.</summary>
    public DbSet<MediaItem> MediaItems { get; set; }

    /// <summary>TV shows in the local library.</summary>
    public DbSet<TvShow> TvShows { get; set; }

    /// <summary>Seasons belonging to TV shows (only downloaded seasons).</summary>
    public DbSet<Season> Seasons { get; set; }

    /// <summary>Individual episodes belonging to seasons.</summary>
    public DbSet<Episode> Episodes { get; set; }

    /// <summary>
    /// Watch progress records for movies and episodes.
    /// Never deleted — preserved even when source files are removed.
    /// </summary>
    public DbSet<WatchProgress> WatchProgress { get; set; }

    /// <summary>
    /// Reserved for the future Skip Intro / Skip Outro feature.
    /// Pre-created so the feature can be added without a schema migration.
    /// </summary>
    public DbSet<IntroTimestamp> IntroTimestamps { get; set; }

    // ── Model Configuration ──────────────────────────────────────────────────

    /// <summary>
    /// Configures entity relationships, indexes, and constraints using the Fluent API.
    /// Called automatically by EF Core when the model is first built.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── MediaItem ────────────────────────────────────────────────────────
        modelBuilder.Entity<MediaItem>(entity =>
        {
            // Ensure no two movies can share the same file path (prevents duplicates on re-scan)
            entity.HasIndex(m => m.FilePath).IsUnique();

            // One movie → one WatchProgress (or none)
            entity.HasOne(m => m.WatchProgress)
                  .WithOne(w => w.MediaItem)
                  .HasForeignKey<WatchProgress>(w => w.MediaItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── TvShow ───────────────────────────────────────────────────────────
        modelBuilder.Entity<TvShow>(entity =>
        {
            // Ensure no duplicate shows by TMDB ID (filter nulls for SQL Server compatibility)
            entity.HasIndex(t => t.TmdbId).IsUnique().HasFilter("\"TmdbId\" IS NOT NULL");
            
            // Ensure no duplicate shows by Title
            entity.HasIndex(t => t.Title).IsUnique();

            // One show → many seasons
            entity.HasMany(t => t.Seasons)
                  .WithOne(s => s.TvShow)
                  .HasForeignKey(s => s.TvShowId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Season ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Season>(entity =>
        {
            // Composite unique index: one season number per show
            entity.HasIndex(s => new { s.TvShowId, s.SeasonNumber }).IsUnique();

            // One season → many episodes
            entity.HasMany(s => s.Episodes)
                  .WithOne(e => e.Season)
                  .HasForeignKey(e => e.SeasonId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Episode ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Episode>(entity =>
        {
            // Ensure no two episodes share the same file path
            entity.HasIndex(e => e.FilePath).IsUnique();

            // One episode → one WatchProgress (or none)
            entity.HasOne(e => e.WatchProgress)
                  .WithOne(w => w.Episode)
                  .HasForeignKey<WatchProgress>(w => w.EpisodeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── WatchProgress ────────────────────────────────────────────────────
        modelBuilder.Entity<WatchProgress>(entity =>
        {
            // Composite unique index: one progress record per media item / episode
            entity.HasIndex(w => new { w.MediaItemId, w.EpisodeId }).IsUnique();

            // Index on LastWatchedAt for fast "Continue Watching" queries (sorted by recency)
            entity.HasIndex(w => w.LastWatchedAt);
        });
    }
}
