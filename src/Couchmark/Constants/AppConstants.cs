namespace Couchmark.Constants;

/// <summary>
/// Application-wide constants. All magic numbers and strings live here —
/// never hard-code values in business logic or UI code.
/// </summary>
public static class AppConstants
{
    // ── Playback ─────────────────────────────────────────────────────────────

    /// <summary>
    /// How often (in seconds) the player saves watch progress to the database.
    /// Lower = more accurate resume, higher = fewer DB writes.
    /// </summary>
    public const int ProgressSaveIntervalSeconds = 5;

    /// <summary>
    /// If the saved position is less than this many seconds from the start,
    /// the player restarts from the beginning instead of resuming.
    /// Prevents resuming from the very first few seconds.
    /// </summary>
    public const int ResumeThresholdSeconds = 30;

    /// <summary>
    /// When playback reaches this percentage of total duration, the item is
    /// automatically marked as "completed" (watched).
    /// e.g. 0.90 = 90% through the video.
    /// </summary>
    public const double CompletedThresholdPercent = 0.90;

    /// <summary>
    /// How many seconds the auto-play countdown shows before advancing
    /// to the next episode.
    /// </summary>
    public const int AutoPlayCountdownSeconds = 10;

    /// <summary>
    /// Number of seconds to seek forward/backward when the ← → keys are pressed.
    /// </summary>
    public const int SeekStepSeconds = 10;

    /// <summary>
    /// How much to adjust the volume (0–100 scale) per ↑ ↓ key press.
    /// </summary>
    public const int VolumeStepPercent = 5;

    /// <summary>
    /// Milliseconds of mouse inactivity before the player overlay auto-hides.
    /// </summary>
    public const int OverlayHideDelayMs = 3000;

    // ── File System ──────────────────────────────────────────────────────────

    /// <summary>
    /// Video file extensions the library scanner will recognise.
    /// All lowercase. Comparison should be case-insensitive.
    /// </summary>
    public static readonly string[] VideoExtensions =
        [".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".webm"];

    /// <summary>
    /// Subtitle file extensions the scanner will look for alongside video files.
    /// </summary>
    public static readonly string[] SubtitleExtensions =
        [".srt", ".ass", ".ssa", ".sub", ".vtt"];

    // ── Metadata / API ───────────────────────────────────────────────────────

    /// <summary>
    /// Base URL for TMDB image CDN. Append a poster_path from the API response.
    /// e.g. AppConstants.TmdbImageBaseUrl + "w342" + posterPath
    /// </summary>
    public const string TmdbImageBaseUrl = "https://image.tmdb.org/t/p/";

    /// <summary>
    /// TMDB poster size used for media cards (poster grid view).
    /// </summary>
    public const string TmdbPosterSizeCard = "w342";

    /// <summary>
    /// TMDB backdrop size used for hero banners and detail page backgrounds.
    /// </summary>
    public const string TmdbBackdropSizeHero = "w1280";

    /// <summary>
    /// Base URL for the OMDB API. Append ?i={imdbId}&apikey={key} for a lookup.
    /// </summary>
    public const string OmdbBaseUrl = "https://www.omdbapi.com/";

    // ── Storage Paths ────────────────────────────────────────────────────────

    /// <summary>
    /// The application name used to create the %AppData%\Couchmark folder.
    /// Change here if the app is ever renamed.
    /// </summary>
    public const string AppName = "Couchmark";

    /// <summary>
    /// Sub-folder inside %AppData%\Couchmark where downloaded poster images are cached.
    /// </summary>
    public const string PosterCacheFolder = "Posters";

    /// <summary>
    /// Sub-folder inside %AppData%\Couchmark where log files are written.
    /// </summary>
    public const string LogsFolder = "Logs";

    /// <summary>
    /// File name of the SQLite database stored in %AppData%\Couchmark\.
    /// </summary>
    public const string DatabaseFileName = "Couchmark.db";
}
