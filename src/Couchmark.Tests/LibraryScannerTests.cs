using Couchmark.Data;
using Couchmark.Data.Entities;
using Couchmark.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Couchmark.Tests;

public class LibraryScannerTests
{
    private sealed class NoOpMetadataService : IMetadataService
    {
        public Task<int?> SearchMovieAsync(string title, int? year = null) => Task.FromResult<int?>(null);
        public Task<int?> SearchTvShowAsync(string title) => Task.FromResult<int?>(null);
        public Task EnrichMovieAsync(MediaItem item) => Task.CompletedTask;
        public Task EnrichTvShowAsync(TvShow show) => Task.CompletedTask;
        public Task EnrichEpisodeAsync(Episode episode, int tvShowTmdbId) => Task.CompletedTask;
        public Task<string?> DownloadPosterAsync(string posterPath, string fileName) => Task.FromResult<string?>(null);
    }

    private static ServiceProvider BuildServices(string dbPath, string[] moviePaths, string[] tvPaths)
    {
        var configData = new Dictionary<string, string?>();
        for (int i = 0; i < moviePaths.Length; i++)
            configData[$"Libraries:MoviePaths:{i}"] = moviePaths[i];
        for (int i = 0; i < tvPaths.Length; i++)
            configData[$"Libraries:TvShowPaths:{i}"] = tvPaths[i];

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton<IMetadataService, NoOpMetadataService>();
        services.AddDbContext<CouchmarkDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ScanAllAsync_EliminatesUnavailableSeriesAndMovies_IncludingWithIntroTimestamps()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"couchmark_scan_test_{Guid.NewGuid()}");
        string tempDb = Path.Combine(tempRoot, "test.db");
        string moviesDir = Path.Combine(tempRoot, "Movies");
        string tvDir = Path.Combine(tempRoot, "TV");
        Directory.CreateDirectory(moviesDir);
        Directory.CreateDirectory(tvDir);

        try
        {
            await using var provider = BuildServices(tempDb, [moviesDir], [tvDir]);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                await db.Database.MigrateAsync();

                var staleMovie = new MediaItem
                {
                    FilePath = Path.Combine(moviesDir, "Gone Movie (2020).mkv"),
                    Title = "Gone Movie",
                    FileAvailable = true
                };
                db.MediaItems.Add(staleMovie);

                var staleShow = new TvShow { Title = "Suits", TotalSeasonsOnTmdb = 9 };
                db.TvShows.Add(staleShow);
                await db.SaveChangesAsync();

                var staleSeason = new Season { TvShowId = staleShow.Id, SeasonNumber = 3, Title = "Season 3" };
                db.Seasons.Add(staleSeason);
                await db.SaveChangesAsync();

                var staleEpisode = new Episode
                {
                    SeasonId = staleSeason.Id,
                    SeasonNumber = 3,
                    EpisodeNumber = 1,
                    Title = "The Arrangement",
                    FilePath = Path.Combine(tvDir, "Suits Season 3", "Suits S03E01.mkv"),
                    FileAvailable = true
                };
                db.Episodes.Add(staleEpisode);
                await db.SaveChangesAsync();

                db.IntroTimestamps.AddRange(
                    new IntroTimestamp { MediaItemId = staleMovie.Id, IntroStartSeconds = 10, IntroEndSeconds = 30 },
                    new IntroTimestamp { EpisodeId = staleEpisode.Id, IntroStartSeconds = 5, IntroEndSeconds = 25 });
                await db.SaveChangesAsync();
            }

            var scanner = new LibraryScanner(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<IMetadataService>(),
                NullLogger<LibraryScanner>.Instance);

            // Both folders are empty -> scan must eliminate the unavailable movie and series completely
            await scanner.ScanAllAsync();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                Assert.Empty(await db.MediaItems.ToListAsync());
                Assert.Empty(await db.TvShows.ToListAsync());
                Assert.Empty(await db.Seasons.ToListAsync());
                Assert.Empty(await db.Episodes.ToListAsync());
                Assert.Empty(await db.IntroTimestamps.ToListAsync());
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ScanAllAsync_KeepsSeriesWhenAtLeastOneSeasonIsAvailable_AndPrunesDeletedSeasons()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"couchmark_partial_test_{Guid.NewGuid()}");
        string tempDb = Path.Combine(tempRoot, "test.db");
        string tvDir = Path.Combine(tempRoot, "TV");
        string s3Dir = Path.Combine(tvDir, "Suits Season 3");
        string s4Dir = Path.Combine(tvDir, "Suits Season 4");
        Directory.CreateDirectory(s3Dir);
        Directory.CreateDirectory(s4Dir);

        string s3Ep = Path.Combine(s3Dir, "Suits S03E01.mkv");
        string s4Ep = Path.Combine(s4Dir, "Suits S04E01.mkv");
        File.WriteAllText(s3Ep, string.Empty);
        File.WriteAllText(s4Ep, string.Empty);

        try
        {
            await using var provider = BuildServices(tempDb, [], [tvDir]);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                await db.Database.MigrateAsync();
            }

            var scanner = new LibraryScanner(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<IMetadataService>(),
                NullLogger<LibraryScanner>.Instance);

            await scanner.ScanAllAsync();

            // Delete Season 3, keep Season 4
            Directory.Delete(s3Dir, recursive: true);
            await scanner.ScanAllAsync();

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                var shows = await db.TvShows.Include(t => t.Seasons).ThenInclude(s => s.Episodes).ToListAsync();
                Assert.Single(shows);
                Assert.Equal("Suits", shows[0].Title);
                Assert.Single(shows[0].Seasons);
                Assert.Equal(4, shows[0].Seasons.First().SeasonNumber);
                Assert.Single(shows[0].Seasons.First().Episodes);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RemoveDirectoryAsync_OnlyMarksExactDirectoryPrefix()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"couchmark_dir_test_{Guid.NewGuid()}");
        string tempDb = Path.Combine(tempRoot, "test.db");
        Directory.CreateDirectory(tempRoot);

        try
        {
            await using var provider = BuildServices(tempDb, [], []);
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                await db.Database.MigrateAsync();

                var show = new TvShow { Title = "Suits" };
                var show2 = new TvShow { Title = "Suits LA" };
                db.TvShows.AddRange(show, show2);
                await db.SaveChangesAsync();

                var season1 = new Season { TvShowId = show.Id, SeasonNumber = 1, Title = "Season 1" };
                var season2 = new Season { TvShowId = show2.Id, SeasonNumber = 1, Title = "Season 1" };
                db.Seasons.AddRange(season1, season2);
                await db.SaveChangesAsync();

                db.Episodes.AddRange(
                    new Episode { SeasonId = season1.Id, SeasonNumber = 1, EpisodeNumber = 1, Title = "Ep1", FilePath = @"C:\Shows\Suits\S01E01.mkv", FileAvailable = true },
                    new Episode { SeasonId = season2.Id, SeasonNumber = 1, EpisodeNumber = 1, Title = "Ep1", FilePath = @"C:\Shows\Suits 2\S01E01.mkv", FileAvailable = true });
                await db.SaveChangesAsync();
            }

            var scanner = new LibraryScanner(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IConfiguration>(),
                provider.GetRequiredService<IMetadataService>(),
                NullLogger<LibraryScanner>.Instance);

            await scanner.RemoveDirectoryAsync(@"C:\Shows\Suits");

            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                var suitsEp = await db.Episodes.FirstAsync(e => e.FilePath == @"C:\Shows\Suits\S01E01.mkv");
                var suits2Ep = await db.Episodes.FirstAsync(e => e.FilePath == @"C:\Shows\Suits 2\S01E01.mkv");
                Assert.False(suitsEp.FileAvailable);
                Assert.True(suits2Ep.FileAvailable);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
    }
}
