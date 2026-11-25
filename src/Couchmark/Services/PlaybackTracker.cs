using Couchmark.Data;
using Couchmark.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Couchmark.Services;

/// <summary>
/// Tracks playback progress and saves it to SQLite every
/// <see cref="Constants.AppConstants.ProgressSaveIntervalSeconds"/> seconds.
///
/// Phase 1: Basic save/load skeleton.
/// Phase 4: Full timer-based auto-save and LibVLC position integration.
/// </summary>
public class PlaybackTracker : IPlaybackTracker, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlaybackTracker> _logger;

    // Tracks the currently active session
    private long? _currentMediaItemId;
    private long? _currentEpisodeId;
    private long _currentPositionSeconds;
    private long _currentDurationSeconds;
    
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public PlaybackTracker(IServiceScopeFactory scopeFactory, ILogger<PlaybackTracker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartTrackingAsync(long? mediaItemId, long? episodeId, long startPositionSeconds)
    {
        await _semaphore.WaitAsync();
        try
        {
            _currentMediaItemId = mediaItemId;
            _currentEpisodeId = episodeId;
            _currentPositionSeconds = startPositionSeconds;
            _currentDurationSeconds = 0;

            _logger.LogInformation(
                "Started tracking: MediaItemId={MediaItemId}, EpisodeId={EpisodeId}, StartPos={Start}s",
                mediaItemId, episodeId, startPositionSeconds);

            // Increment play count
            await using var scope = _scopeFactory.CreateAsyncScope();
            CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

            WatchProgress progress = await GetOrCreateProgressAsync(db, mediaItemId, episodeId);
            progress.PlayCount++;
            progress.LastWatchedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpdatePositionAsync(long positionSeconds, long durationSeconds)
    {
        long? mediaId;
        long? episodeId;

        await _semaphore.WaitAsync();
        try
        {
            _currentPositionSeconds = positionSeconds;
            _currentDurationSeconds = durationSeconds;
            
            mediaId = _currentMediaItemId;
            episodeId = _currentEpisodeId;
        }
        finally
        {
            _semaphore.Release();
        }

        if (mediaId is null && episodeId is null)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        WatchProgress progress = await GetOrCreateProgressAsync(db, mediaId, episodeId);

        progress.PositionSeconds = positionSeconds;
        progress.DurationSeconds = durationSeconds;
        progress.LastWatchedAt = DateTime.UtcNow;

        // Auto-mark as completed if past the threshold (e.g. 90%)
        if (durationSeconds > 0)
        {
            double percent = (double)positionSeconds / durationSeconds;
            if (percent >= Constants.AppConstants.CompletedThresholdPercent)
            {
                progress.IsCompleted = true;
                _logger.LogInformation("Item marked as completed (watched {Percent:P0})", percent);
            }
        }

        await db.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task StopTrackingAsync()
    {
        long? mediaId;
        long? episodeId;
        long pos;
        long dur;

        await _semaphore.WaitAsync();
        try
        {
            mediaId = _currentMediaItemId;
            episodeId = _currentEpisodeId;
            pos = _currentPositionSeconds;
            dur = _currentDurationSeconds;
            
            _currentMediaItemId = null;
            _currentEpisodeId = null;
        }
        finally
        {
            _semaphore.Release();
        }

        if (mediaId is null && episodeId is null)
        {
            return; // Nothing is being tracked
        }

        // Final position flush to database
        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        WatchProgress progress = await GetOrCreateProgressAsync(db, mediaId, episodeId);

        progress.PositionSeconds = pos;
        progress.DurationSeconds = dur;
        progress.LastWatchedAt = DateTime.UtcNow;

        if (dur > 0 && (double)pos / dur >= Constants.AppConstants.CompletedThresholdPercent)
        {
            progress.IsCompleted = true;
        }

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Stopped tracking at position {Pos}s", pos);
    }

    /// <inheritdoc/>
    public async Task<WatchProgress?> GetProgressAsync(long? mediaItemId, long? episodeId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        return await db.WatchProgress
            .AsNoTracking()
            .FirstOrDefaultAsync(w =>
                w.MediaItemId == mediaItemId &&
                w.EpisodeId == episodeId);
    }

    /// <inheritdoc/>
    public async Task SetWatchedAsync(long? mediaItemId, long? episodeId, bool isWatched)
    {
        if (mediaItemId is null && episodeId is null) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        WatchProgress progress = await GetOrCreateProgressAsync(db, mediaItemId, episodeId);
        progress.IsCompleted = isWatched;
        if (!isWatched)
        {
            progress.PositionSeconds = 0;
        }
        progress.LastWatchedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Manually set watched={Watched} for MediaItemId={MediaItemId}, EpisodeId={EpisodeId}",
            isWatched, mediaItemId, episodeId);
    }

    /// <inheritdoc/>
    public async Task SetEpisodesWatchedAsync(IEnumerable<long> episodeIds, bool isWatched)
    {
        var ids = episodeIds.Distinct().ToList();
        if (ids.Count == 0) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        var existingRecords = await db.WatchProgress
            .Where(w => w.EpisodeId != null && ids.Contains(w.EpisodeId.Value))
            .ToDictionaryAsync(w => w.EpisodeId!.Value);

        DateTime now = DateTime.UtcNow;
        foreach (long epId in ids)
        {
            if (!existingRecords.TryGetValue(epId, out WatchProgress? progress))
            {
                progress = new WatchProgress { EpisodeId = epId };
                db.WatchProgress.Add(progress);
            }

            progress.IsCompleted = isWatched;
            if (!isWatched)
            {
                progress.PositionSeconds = 0;
            }
            progress.LastWatchedAt = now;
        }

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Batch set watched={Watched} for {Count} episodes",
            isWatched, ids.Count);
    }

    /// <inheritdoc/>
    public async Task ResetProgressAsync(long? mediaItemId, long? episodeId)
    {
        if (mediaItemId is null && episodeId is null) return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        WatchProgress progress = await GetOrCreateProgressAsync(db, mediaItemId, episodeId);
        progress.PositionSeconds = 0;
        progress.IsCompleted = false;
        progress.LastWatchedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Reset progress to 0 for MediaItemId={MediaItemId}, EpisodeId={EpisodeId}",
            mediaItemId, episodeId);
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the existing WatchProgress record for the given item, or creates a new one.
    /// The new record is added to the context but NOT yet saved — caller must SaveChangesAsync.
    /// </summary>
    private static async Task<WatchProgress> GetOrCreateProgressAsync(
        CouchmarkDbContext db, long? mediaItemId, long? episodeId)
    {
        WatchProgress? existing = await db.WatchProgress
            .FirstOrDefaultAsync(w =>
                w.MediaItemId == mediaItemId &&
                w.EpisodeId == episodeId);

        if (existing is not null)
        {
            return existing;
        }

        // No record yet — create one
        WatchProgress newProgress = new()
        {
            MediaItemId = mediaItemId,
            EpisodeId = episodeId
        };

        db.WatchProgress.Add(newProgress);
        return newProgress;
    }

    /// <summary>Ensures any in-progress session is flushed on disposal.</summary>
    public async ValueTask DisposeAsync()
    {
        await StopTrackingAsync();
        _semaphore.Dispose();
    }
}
