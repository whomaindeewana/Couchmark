namespace Couchmark.Data.Entities;

/// <summary>
/// Represents a single episode of a TV show.
/// Created from a video file whose name matches the SxxExx pattern
/// inside a season folder (e.g. "Suits S03E07.mkv").
/// </summary>
public class Episode
{
    /// <summary>Primary key — auto-incremented by SQLite.</summary>
    public long Id { get; set; }

    /// <summary>Foreign key — the season this episode belongs to.</summary>
    public long SeasonId { get; set; }

    /// <summary>Season number (denormalised for easy querying). e.g. 3</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Episode number within the season. e.g. 7</summary>
    public int EpisodeNumber { get; set; }

    /// <summary>
    /// Absolute path to the video file on disk.
    /// e.g. "D:\Shows\Suits Season 3\Suits S03E07.mkv"
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Path to a matching external subtitle file (same folder, same base name).
    /// e.g. "D:\Shows\Suits Season 3\Suits S03E07.srt"
    /// Null if no subtitle file was found.
    /// </summary>
    public string? ExternalSubtitlePath { get; set; }

    /// <summary>
    /// False when the video file no longer exists on disk (e.g. user deleted the season).
    /// The record is kept so watch history (✓ watched badge) is preserved.
    /// </summary>
    public bool FileAvailable { get; set; } = true;

    /// <summary>The episode's ID on TMDB, used to fetch episode-level metadata.</summary>
    public int? TmdbId { get; set; }

    /// <summary>Episode title from TMDB. e.g. "The Other Time"</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Episode synopsis from TMDB.</summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// Air date string from TMDB. e.g. "2013-07-16"
    /// Stored as string for flexibility; parse to DateTime when needed.
    /// </summary>
    public string AirDate { get; set; } = string.Empty;

    /// <summary>Episode runtime in minutes from TMDB.</summary>
    public int RuntimeMinutes { get; set; }

    /// <summary>
    /// Local cache path to the TMDB episode still image (160×90 thumbnail).
    /// e.g. "%AppData%\Couchmark\cache\stills\episode_42.jpg"
    /// Null until the metadata enrichment service downloads the still.
    /// </summary>
    public string? StillPath { get; set; }

    /// <summary>UTC timestamp when this episode was first detected by the scanner.</summary>
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    // ── Navigation Properties ────────────────────────────────────────────────

    /// <summary>EF Core navigation — the parent season.</summary>
    public Season Season { get; set; } = null!;

    /// <summary>
    /// EF Core navigation — the watch progress for this specific episode.
    /// Null if this episode has never been opened.
    /// </summary>
    public WatchProgress? WatchProgress { get; set; }
}
