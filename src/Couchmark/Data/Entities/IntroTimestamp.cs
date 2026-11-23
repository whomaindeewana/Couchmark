namespace Couchmark.Data.Entities;

/// <summary>
/// Reserved for the future Skip Intro / Skip Outro feature.
/// Stores the timestamp windows for a movie or episode where
/// the intro and outro occur. The player will show a "Skip" button
/// when playback enters these ranges.
///
/// This table is created now so the feature can be added later
/// without a database migration affecting existing data.
/// </summary>
public class IntroTimestamp
{
    /// <summary>Primary key — auto-incremented by SQLite.</summary>
    public long Id { get; set; }

    /// <summary>
    /// Foreign key to MediaItem (movie intro). Null for episode records.
    /// Exactly one of MediaItemId or EpisodeId will be set.
    /// </summary>
    public long? MediaItemId { get; set; }

    /// <summary>
    /// Foreign key to Episode. Null for movie records.
    /// For TV shows, the same intro timestamps typically apply to all episodes
    /// in a season — a separate record per episode allows per-episode overrides.
    /// </summary>
    public long? EpisodeId { get; set; }

    /// <summary>Second at which the intro starts. e.g. 0 (cold open → intro begins at 0:00)</summary>
    public int IntroStartSeconds { get; set; }

    /// <summary>Second at which the intro ends (player will seek here when Skip is clicked).</summary>
    public int IntroEndSeconds { get; set; }

    /// <summary>Second at which the outro / credits start. e.g. 2580 (43 minutes in)</summary>
    public int OutroStartSeconds { get; set; }

    /// <summary>Second at which the outro ends. Usually equals the video duration.</summary>
    public int OutroEndSeconds { get; set; }

    // ── Navigation Properties ────────────────────────────────────────────────

    /// <summary>EF Core navigation — the parent movie. Null for episode records.</summary>
    public MediaItem? MediaItem { get; set; }

    /// <summary>EF Core navigation — the parent episode. Null for movie records.</summary>
    public Episode? Episode { get; set; }
}
