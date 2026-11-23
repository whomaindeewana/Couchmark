using Couchmark.Data;
using Couchmark.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Couchmark.Tests;

public class IntroTimestampTests
{
    [Fact]
    public async Task IntroTimestamp_CanBeCreatedAndRetrievedForMovie()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"couchmark_intro_test_{Guid.NewGuid()}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CouchmarkDbContext>()
                .UseSqlite($"Data Source={tempDb}")
                .Options;

            await using (var db = new CouchmarkDbContext(options))
            {
                await db.Database.MigrateAsync();

                var movie = new MediaItem
                {
                    FilePath = "C:\\Movies\\Inception.mkv",
                    Title = "Inception",
                    Overview = "Dream within a dream",
                    Genres = "Sci-Fi",
                    ImdbRating = "8.8"
                };
                db.MediaItems.Add(movie);
                await db.SaveChangesAsync();

                var intro = new IntroTimestamp
                {
                    MediaItemId = movie.Id,
                    IntroStartSeconds = 60,
                    IntroEndSeconds = 120,
                    OutroStartSeconds = 8000,
                    OutroEndSeconds = 8500
                };
                db.IntroTimestamps.Add(intro);
                await db.SaveChangesAsync();
            }

            await using (var db = new CouchmarkDbContext(options))
            {
                var intro = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.IntroStartSeconds == 60);
                Assert.NotNull(intro);
                Assert.Equal(120, intro.IntroEndSeconds);
                Assert.Equal(8000, intro.OutroStartSeconds);
                Assert.Equal(8500, intro.OutroEndSeconds);
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

    [Fact]
    public async Task IntroTimestamp_CanApplyToAllEpisodesInSeason()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"couchmark_intro_season_test_{Guid.NewGuid()}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CouchmarkDbContext>()
                .UseSqlite($"Data Source={tempDb}")
                .Options;

            await using (var db = new CouchmarkDbContext(options))
            {
                await db.Database.MigrateAsync();

                var show = new TvShow { Title = "Suits", Overview = "Law drama" };
                db.TvShows.Add(show);
                await db.SaveChangesAsync();

                var season = new Season { TvShowId = show.Id, SeasonNumber = 1, Title = "Season 1" };
                db.Seasons.Add(season);
                await db.SaveChangesAsync();

                var ep1 = new Episode { SeasonId = season.Id, SeasonNumber = 1, EpisodeNumber = 1, Title = "Pilot", FilePath = "C:\\Shows\\S01E01.mkv" };
                var ep2 = new Episode { SeasonId = season.Id, SeasonNumber = 1, EpisodeNumber = 2, Title = "Errors", FilePath = "C:\\Shows\\S01E02.mkv" };
                db.Episodes.AddRange(ep1, ep2);
                await db.SaveChangesAsync();

                // Bulk apply intro timestamps to all episodes in season
                var episodes = await db.Episodes.Where(e => e.SeasonId == season.Id).ToListAsync();
                foreach (var ep in episodes)
                {
                    db.IntroTimestamps.Add(new IntroTimestamp
                    {
                        EpisodeId = ep.Id,
                        IntroStartSeconds = 90,
                        IntroEndSeconds = 145,
                        OutroStartSeconds = 2500,
                        OutroEndSeconds = 2600
                    });
                }
                await db.SaveChangesAsync();
            }

            await using (var db = new CouchmarkDbContext(options))
            {
                var allIntro = await db.IntroTimestamps.ToListAsync();
                Assert.Equal(2, allIntro.Count);
                Assert.All(allIntro, it => Assert.Equal(90, it.IntroStartSeconds));
                Assert.All(allIntro, it => Assert.Equal(145, it.IntroEndSeconds));
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
