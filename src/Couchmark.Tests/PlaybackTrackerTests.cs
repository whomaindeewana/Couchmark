using Couchmark.Data;
using Couchmark.Data.Entities;
using Couchmark.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Couchmark.Tests;

public class PlaybackTrackerTests
{
    private static ServiceProvider BuildServices(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CouchmarkDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SetWatchedAsync_WhenMarkingUnwatched_ResetsPositionToZero()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"couchmark_tracker_test_{Guid.NewGuid()}.db");
        try
        {
            await using var provider = BuildServices(tempDb);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                await db.Database.MigrateAsync();

                db.MediaItems.Add(new MediaItem
                {
                    FilePath = "C:\\Movies\\Matrix.mkv",
                    Title = "The Matrix",
                    Overview = "Red pill",
                    Genres = "Sci-Fi",
                    ImdbRating = "8.7"
                });
                await db.SaveChangesAsync();
            }

            long movieId;
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                movieId = (await db.MediaItems.FirstAsync()).Id;
            }

            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            await using var tracker = new PlaybackTracker(scopeFactory, NullLogger<PlaybackTracker>.Instance);

            await tracker.StartTrackingAsync(movieId, null, 0);
            await tracker.UpdatePositionAsync(3600, 7200);
            await tracker.StopTrackingAsync();

            var midProgress = await tracker.GetProgressAsync(movieId, null);
            Assert.NotNull(midProgress);
            Assert.Equal(3600, midProgress!.PositionSeconds);
            Assert.False(midProgress.IsCompleted);

            await tracker.SetWatchedAsync(movieId, null, true);
            var watchedProgress = await tracker.GetProgressAsync(movieId, null);
            Assert.True(watchedProgress!.IsCompleted);

            await tracker.SetWatchedAsync(movieId, null, false);
            var unwatchedProgress = await tracker.GetProgressAsync(movieId, null);
            Assert.False(unwatchedProgress!.IsCompleted);
            Assert.Equal(0, unwatchedProgress.PositionSeconds);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    [Fact]
    public async Task SetEpisodesWatchedAsync_UpdatesAllEpisodesInBatch()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"couchmark_batch_test_{Guid.NewGuid()}.db");
        try
        {
            await using var provider = BuildServices(tempDb);
            long[] episodeIds;
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                await db.Database.MigrateAsync();

                var show = new TvShow { Title = "Severance", Overview = "Work life balance" };
                db.TvShows.Add(show);
                await db.SaveChangesAsync();

                var season = new Season { TvShowId = show.Id, SeasonNumber = 1, Title = "Season 1" };
                db.Seasons.Add(season);
                await db.SaveChangesAsync();

                var ep1 = new Episode { SeasonId = season.Id, SeasonNumber = 1, EpisodeNumber = 1, Title = "Ep 1", FilePath = "C:\\Shows\\S01E01.mkv" };
                var ep2 = new Episode { SeasonId = season.Id, SeasonNumber = 1, EpisodeNumber = 2, Title = "Ep 2", FilePath = "C:\\Shows\\S01E02.mkv" };
                db.Episodes.AddRange(ep1, ep2);
                await db.SaveChangesAsync();

                db.WatchProgress.Add(new WatchProgress { EpisodeId = ep1.Id, PositionSeconds = 500, DurationSeconds = 3000, IsCompleted = false });
                await db.SaveChangesAsync();

                episodeIds = [ep1.Id, ep2.Id];
            }

            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            await using var tracker = new PlaybackTracker(scopeFactory, NullLogger<PlaybackTracker>.Instance);

            await tracker.SetEpisodesWatchedAsync(episodeIds, true);

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                var progresses = await db.WatchProgress.Where(w => w.EpisodeId != null).ToListAsync();
                Assert.Equal(2, progresses.Count);
                Assert.All(progresses, p => Assert.True(p.IsCompleted));
            }

            await tracker.SetEpisodesWatchedAsync(episodeIds, false);

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                var progresses = await db.WatchProgress.Where(w => w.EpisodeId != null).ToListAsync();
                Assert.Equal(2, progresses.Count);
                Assert.All(progresses, p =>
                {
                    Assert.False(p.IsCompleted);
                    Assert.Equal(0, p.PositionSeconds);
                });
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }
}
