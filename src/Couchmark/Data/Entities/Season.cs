namespace Couchmark.Data.Entities;

/// <summary>
/// Represents a single season of a TV show in the local library.
/// A Season is created when the scanner finds a matching folder
/// like "Suits Season 3\" on disk.
/// </summary>
public class Season
{
    /// <summary>Primary key — auto-incremented by SQLite.</summary>
    public long Id { get; set; }

    /// <summary>Foreign key — the parent TV show this season belongs to.</summary>
    public long TvShowId { get; set; }

    /// <summary>Season number. e.g. 3 (from "Suits Season 3" folder name).</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Season title from TMDB, if available. e.g. "Season 3"</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Season-level overview from TMDB, if available.</summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>Year this season first aired, from TMDB.</summary>
    public int AirYear { get; set; }

    /// <summary>UTC timestamp when this season was first detected by the scanner.</summary>
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    // ── Navigation Properties ────────────────────────────────────────────────

    /// <summary>EF Core navigation — the parent TV show.</summary>
    public TvShow TvShow { get; set; } = null!;

    /// <summary>EF Core navigation — all episodes in this season.</summary>
    public ICollection<Episode> Episodes { get; set; } = [];
}
