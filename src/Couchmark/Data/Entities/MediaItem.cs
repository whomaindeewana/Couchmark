namespace Couchmark.Data.Entities;

/// <summary>
/// Represents a single movie in the local library.
/// Populated by the LibraryScanner and enriched by the MetadataService.
/// </summary>
public class MediaItem
{
    /// <summary>Primary key — auto-incremented by SQLite.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Absolute path to the video file on disk.
    /// e.g. "D:\Movies\The Dark Knight (2008).mkv"
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Path to a matching subtitle file (same folder, same base name).
    /// Null if no subtitle file was found during scanning.
    /// e.g. "D:\Movies\The Dark Knight (2008).srt"
    /// </summary>
    public string? ExternalSubtitlePath { get; set; }

    /// <summary>
    /// False when the file no longer exists on disk (e.g. user deleted it).
    /// The record and watch history are kept so progress is never lost.
    /// </summary>
    public bool FileAvailable { get; set; } = true;

    /// <summary>The movie's ID on The Movie Database (TMDB).</summary>
    public int? TmdbId { get; set; }

    /// <summary>
    /// The movie's IMDB ID (tt-number), fetched via TMDB then used for OMDB lookup.
    /// e.g. "tt0468569"
    /// </summary>
    public string? ImdbId { get; set; }

    /// <summary>Movie title as returned by TMDB.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Plot synopsis from TMDB.</summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// TMDB poster path (relative, not a full URL).
    /// Use with AppConstants.TmdbImageBaseUrl + size + PosterPath to build the URL.
    /// e.g. "/7WsyChQLEftFiDOVTGkv4z5Fjcr.jpg"
    /// </summary>
    public string? PosterPath { get; set; }

    /// <summary>
    /// Local path to the cached poster image in %AppData%\Couchmark\Posters\.
    /// Null until the poster has been downloaded.
    /// </summary>
    public string? LocalPosterPath { get; set; }

    /// <summary>
    /// TMDB backdrop path (relative). Used for hero banners and detail backgrounds.
    /// </summary>
    public string? BackdropPath { get; set; }

    /// <summary>
    /// Comma-separated genre names from TMDB. e.g. "Action,Crime,Drama"
    /// Stored as a string for simplicity; parsed into a list in the ViewModel.
    /// </summary>
    public string Genres { get; set; } = string.Empty;

    /// <summary>Four-digit release year. e.g. 2008</summary>
    public int ReleaseYear { get; set; }

    /// <summary>Runtime in minutes from TMDB.</summary>
    public int RuntimeMinutes { get; set; }

    /// <summary>TMDB audience score (0.0 – 10.0).</summary>
    public double TmdbRating { get; set; }

    /// <summary>
    /// IMDB rating string fetched from OMDB. e.g. "9.0"
    /// Stored as a string to handle "N/A" and other non-numeric responses gracefully.
    /// </summary>
    public string ImdbRating { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this item was first added to the library.</summary>
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the last time this item was opened in the player.
    /// Null if never watched.
    /// </summary>
    public DateTime? LastWatched { get; set; }

    // ── Navigation Properties ────────────────────────────────────────────────

    /// <summary>
    /// EF Core navigation property — the watch progress record for this movie.
    /// One-to-one relationship (a movie has at most one progress record).
    /// </summary>
    public WatchProgress? WatchProgress { get; set; }
}
