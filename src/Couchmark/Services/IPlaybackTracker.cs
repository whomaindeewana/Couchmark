using Couchmark.Data.Entities;

namespace Couchmark.Services;

/// <summary>
/// Tracks playback progress for movies and episodes.
/// Saves the current position to SQLite every <see cref="Constants.AppConstants.ProgressSaveIntervalSeconds"/> seconds
/// during playback, so the user can resume exactly where they left off.
/// </summary>
public interface IPlaybackTracker
{
    /// <summary>
    /// Starts a tracking session for a movie or episode.
    /// Increments the play count and records the session start time.
    /// Call this when the player begins playback.
    /// </summary>
    /// <param name="mediaItemId">ID of the movie being played. Null for TV episodes.</param>
    /// <param name="episodeId">ID of the episode being played. Null for movies.</param>
    /// <param name="startPositionSeconds">
    /// The position (in seconds) the player is starting from.
    /// Will be > 0 if the user is resuming from a saved position.
    /// </param>
    Task StartTrackingAsync(long? mediaItemId, long? episodeId, long startPositionSeconds);

    /// <summary>
    /// Updates the saved position during playback.
    /// Called on a timer every <see cref="Constants.AppConstants.ProgressSaveIntervalSeconds"/> seconds.
    /// Also checks if the completion threshold has been reached and marks the item as watched.
    /// </summary>
    /// <param name="positionSeconds">Current playback position in seconds.</param>
    /// <param name="durationSeconds">Total video duration in seconds.</param>
    Task UpdatePositionAsync(long positionSeconds, long durationSeconds);

    /// <summary>
    /// Finalises the tracking session.
    /// Performs one last save to the database with the exact stop position.
    /// Call this when the player is closed or navigates away.
    /// </summary>
    Task StopTrackingAsync();

    /// <summary>
    /// Retrieves the saved watch progress for a movie or episode.
    /// Returns null if the item has never been played.
    /// </summary>
    /// <param name="mediaItemId">Movie ID to look up. Null for episodes.</param>
    /// <param name="episodeId">Episode ID to look up. Null for movies.</param>
    Task<WatchProgress?> GetProgressAsync(long? mediaItemId, long? episodeId);

    /// <summary>
    /// Manually marks an item as watched (IsCompleted = true) or unwatched (IsCompleted = false).
    /// When marked unwatched, saved position is reset to 0.
    /// Used by the right-click context menu "Mark as Watched / Unwatched" option.
    /// </summary>
    /// <param name="mediaItemId">Movie ID. Null for episodes.</param>
    /// <param name="episodeId">Episode ID. Null for movies.</param>
    /// <param name="isWatched">True to mark watched, false to mark unwatched.</param>
    Task SetWatchedAsync(long? mediaItemId, long? episodeId, bool isWatched);

    /// <summary>
    /// Marks a collection of episodes as watched or unwatched in a single database transaction.
    /// When marked unwatched, saved positions are reset to 0.
    /// </summary>
    Task SetEpisodesWatchedAsync(IEnumerable<long> episodeIds, bool isWatched);

    /// <summary>
    /// Resets the watch progress of a movie or episode back to 0.
    /// Used when restarting playback from the beginning.
    /// </summary>
    /// <param name="mediaItemId">Movie ID to reset. Null for episodes.</param>
    /// <param name="episodeId">Episode ID to reset. Null for movies.</param>
    Task ResetProgressAsync(long? mediaItemId, long? episodeId);
}
