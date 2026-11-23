namespace Couchmark.Data.Entities;

/// <summary>
/// Tracks how far the user has watched a movie or episode.
/// One record per movie (MediaItem) or per episode (Episode).
/// This record is NEVER deleted — even if the source file is removed from disk.
/// </summary>
public class WatchProgress
{
    /// <summary>Primary key — auto-incremented by SQLite.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign key to MediaItem (movie). Null if this is an episode progress record.
    /// Exactly one of MediaItemId or EpisodeId will be set.
    /// </summary>
    public long? MediaItemId { get; set; }

    /// <summary>
    /// Foreign key to Episode. Null if this is a movie progress record.
    /// Exactly one of MediaItemId or EpisodeId will be set.
    /// </summary>
    public long? EpisodeId { get; set; }

    /// <summary>
    /// Playback position in seconds when the user last stopped watching.
    /// The player will seek to this position when resuming.
    /// </summary>
    public long PositionSeconds { get; set; }

    /// <summary>
    /// Total duration of the video in seconds (recorded at first play).
    /// Used to calculate completion percentage without re-reading the file.
    /// </summary>
    public long DurationSeconds { get; set; }

    /// <summary>
    /// True when the user has watched past AppConstants.CompletedThresholdPercent
    /// OR manually marked the item as watched via the context menu.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// UTC timestamp of the last time this item was played.
    /// Used to sort the "Continue Watching" row by recency.
    /// </summary>
    public DateTime LastWatchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total number of times this item has been played (started).
    /// Incremented each time the player opens this item.
    /// </summary>
    public int PlayCount { get; set; }

    // ── Navigation Properties ────────────────────────────────────────────────

    /// <summary>EF Core navigation — the parent movie. Null for episode records.</summary>
    public MediaItem? MediaItem { get; set; }

    /// <summary>EF Core navigation — the parent episode. Null for movie records.</summary>
    public Episode? Episode { get; set; }

    // ── Computed Helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Completion percentage as a value between 0.0 and 1.0.
    /// Returns 0 if duration is unknown. Used for progress bar width binding.
    /// </summary>
    public double CompletionPercent =>
        DurationSeconds > 0 ? (double)PositionSeconds / DurationSeconds : 0.0;
}
