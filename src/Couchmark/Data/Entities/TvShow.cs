namespace Couchmark.Data.Entities;

/// <summary>
/// Represents a TV show in the local library.
/// A single TvShow record groups all seasons and episodes the user has downloaded.
/// </summary>
public class TvShow
{
    /// <summary>Primary key — auto-incremented by SQLite.</summary>
    public long Id { get; set; }

    /// <summary>The show's ID on The Movie Database (TMDB).</summary>
    public int? TmdbId { get; set; }

    /// <summary>
    /// The show's IMDB ID (tt-number), used for fetching ratings via OMDB.
    /// </summary>
    public string? ImdbId { get; set; }

    /// <summary>Show title as returned by TMDB. e.g. "Suits"</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Show synopsis / overview from TMDB.</summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// TMDB poster path (relative). Build full URL with AppConstants.TmdbImageBaseUrl.
    /// </summary>
    public string? PosterPath { get; set; }

    /// <summary>Local path to the cached poster in %AppData%\Couchmark\Posters\.</summary>
    public string? LocalPosterPath { get; set; }

    /// <summary>TMDB backdrop path (relative). Used for hero banners.</summary>
    public string? BackdropPath { get; set; }

    /// <summary>
    /// Comma-separated genre names from TMDB. e.g. "Drama,Legal"
    /// </summary>
    public string Genres { get; set; } = string.Empty;

    /// <summary>Year the show first aired. e.g. 2011</summary>
    public int FirstAirYear { get; set; }

    /// <summary>TMDB audience score (0.0 – 10.0).</summary>
    public double TmdbRating { get; set; }

    /// <summary>
    /// IMDB rating string from OMDB. e.g. "8.4"
    /// Stored as string to handle "N/A" gracefully.
    /// </summary>
    public string ImdbRating { get; set; } = string.Empty;

    /// <summary>
    /// Total number of seasons that exist for this show on TMDB (not just downloaded ones).
    /// Used to display "X seasons total — you have Season Y" in the UI.
    /// </summary>
    public int TotalSeasonsOnTmdb { get; set; }

    /// <summary>UTC timestamp when this show was first added to the library.</summary>
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the last time any episode of this show was watched.
    /// Null if never watched.
    /// </summary>
    public DateTime? LastWatched { get; set; }

    // ── Navigation Properties ────────────────────────────────────────────────

    /// <summary>
    /// EF Core navigation — all Season records that belong to this show.
    /// Only seasons the user has downloaded will exist here.
    /// </summary>
    public ICollection<Season> Seasons { get; set; } = [];
}
