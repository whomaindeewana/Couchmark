using Couchmark.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Couchmark.Tests;

public class DatabaseMigrationTests
{
    [Fact]
    public async Task MigrateAsync_AppliesAllMigrationsSuccessfully()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"couchmark_test_{Guid.NewGuid()}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CouchmarkDbContext>()
                .UseSqlite($"Data Source={tempDb}")
                .Options;

            await using (var db = new CouchmarkDbContext(options))
            {
                await db.Database.MigrateAsync();

                var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
                Assert.Contains(appliedMigrations, m => m.Contains("InitialCreate"));
                Assert.Contains(appliedMigrations, m => m.Contains("AddEpisodeStillPath"));

                // Verify we can insert and query entities
                db.MediaItems.Add(new Couchmark.Data.Entities.MediaItem
                {
                    FilePath = "C:\\Movies\\Test.mkv",
                    Title = "Test Movie",
                    Overview = "Test Overview",
                    Genres = "Action",
                    ImdbRating = "8.0"
                });
                await db.SaveChangesAsync();
            }

            await using (var db = new CouchmarkDbContext(options))
            {
                var item = await db.MediaItems.FirstOrDefaultAsync(m => m.Title == "Test Movie");
                Assert.NotNull(item);
                Assert.Equal("C:\\Movies\\Test.mkv", item.FilePath);
            }
        }
        finally
        {
            // Clear SQLite connection pool to release lock before deleting file
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }
}
