using Couchmark.Data;
using Couchmark.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Couchmark.Services;

/// <summary>
/// Scans configured library folders for video files, identifies them as
/// movies or TV show episodes, and upserts records into the SQLite database.
/// </summary>
public partial class LibraryScanner : ILibraryScanner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IMetadataService _metadataService;
    private readonly ILogger<LibraryScanner> _logger;

    [GeneratedRegex(@"^(.+?)\s+Season\s+(\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonFolderRegex();

    [GeneratedRegex(@"^Season\s+(\d{1,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex PlexSeasonFolderRegex();

    [GeneratedRegex(@"[Ss](\d{1,2})[Ee](\d{1,2})(?:\s*[-–]\s*(.+))?")]
    private static partial Regex EpisodeFileRegex();

    [GeneratedRegex(@"[\(\.]?(19|20)(\d{2})[\)\.\s]?")]
    private static partial Regex MovieYearRegex();

    [GeneratedRegex(@"^(.+?)\s*[Ss](\d{1,2})[Ee](\d{1,2})(?:\s*[-–]\s*(.+))?", RegexOptions.IgnoreCase)]
    private static partial Regex DirectEpisodeRegex();

    [GeneratedRegex(@"(19|20)(\d{2})")]
    private static partial Regex YearDigitsRegex();

    public LibraryScanner(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IMetadataService metadataService,
        ILogger<LibraryScanner> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _metadataService = metadataService;
        _logger = logger;
    }

    public async Task ScanAllAsync(
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting full library scan.");

        string[] moviePaths = _configuration
            .GetSection("Libraries:MoviePaths")
            .Get<string[]>() ?? [];

        string[] tvShowPaths = _configuration
            .GetSection("Libraries:TvShowPaths")
            .Get<string[]>() ?? [];

        List<(string filePath, bool isTv)> allFiles = [];

        foreach (string path in moviePaths)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning("Movie library path not found, skipping: {Path}", path);
                continue;
            }

            try
            {
                var files = Directory
                    .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Where(f => Constants.AppConstants.VideoExtensions
                        .Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var file in files) allFiles.Add((file, false));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access while scanning path: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating movie files in path: {Path}", path);
            }
        }

        foreach (string path in tvShowPaths)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning("TV library path not found, skipping: {Path}", path);
                continue;
            }

            try
            {
                var files = Directory
                    .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Where(f => Constants.AppConstants.VideoExtensions
                        .Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var file in files) allFiles.Add((file, true));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access while scanning path: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating TV files in path: {Path}", path);
            }
        }

        _logger.LogInformation("Found {Count} video files to process.", allFiles.Count);

        for (int i = 0; i < allFiles.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var (filePath, isTv) = allFiles[i];
            progress?.Report(new ScanProgress(i + 1, allFiles.Count, Path.GetFileName(filePath)));

            try
            {
                if (isTv)
                {
                    await ProcessTvFileAsync(filePath, cancellationToken);
                }
                else
                {
                    await ProcessMovieFileAsync(filePath, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {FilePath}", filePath);
            }
        }

        await ReconcileAsync(allFiles.Select(f => f.filePath).ToList(), cancellationToken);

        _logger.LogInformation("Library scan complete. Processed {Count} files.", allFiles.Count);
    }

    public async Task AddFileAsync(string filePath)
    {
        _logger.LogInformation("Adding new file detected by FileSystemWatcher: {File}", filePath);

        string[] tvShowPaths = _configuration.GetSection("Libraries:TvShowPaths").Get<string[]>() ?? [];
        if (tvShowPaths.Any(p => filePath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await ProcessTvFileAsync(filePath, CancellationToken.None);
            return;
        }

        string[] moviePaths = _configuration.GetSection("Libraries:MoviePaths").Get<string[]>() ?? [];
        if (moviePaths.Any(p => filePath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await ProcessMovieFileAsync(filePath, CancellationToken.None);
            return;
        }

        await ProcessFileAsync(filePath, CancellationToken.None);
    }

    public async Task RemoveFileAsync(string filePath)
    {
        _logger.LogInformation("File removed, marking unavailable: {File}", filePath);

        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        MediaItem? movie = await db.MediaItems
            .FirstOrDefaultAsync(m => m.FilePath == filePath);

        if (movie is not null)
        {
            movie.FileAvailable = false;
            await db.SaveChangesAsync();
            return;
        }

        Episode? episode = await db.Episodes
            .FirstOrDefaultAsync(e => e.FilePath == filePath);

        if (episode is not null)
        {
            episode.FileAvailable = false;
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveDirectoryAsync(string directoryPath)
    {
        _logger.LogInformation("Directory removed, marking contents unavailable: {Dir}", directoryPath);

        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        var movies = await db.MediaItems
            .Where(m => m.FilePath.StartsWith(directoryPath))
            .ToListAsync();

        foreach (var m in movies)
        {
            m.FileAvailable = false;
        }

        var episodes = await db.Episodes
            .Where(e => e.FilePath.StartsWith(directoryPath))
            .ToListAsync();

        foreach (var ep in episodes)
        {
            ep.FileAvailable = false;
        }

        if (movies.Count > 0 || episodes.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    private async Task ProcessMovieFileAsync(string filePath, CancellationToken cancellationToken)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        await UpsertMovieAsync(filePath, fileName, cancellationToken);
    }

    private async Task ProcessTvFileAsync(string filePath, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string folderName = Path.GetFileName(directory);
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        // Pattern 1: Folder like "Show Name Season 1"
        Match seasonFolderMatch = SeasonFolderRegex().Match(folderName);
        if (seasonFolderMatch.Success)
        {
            string showTitle = CleanShowTitle(seasonFolderMatch.Groups[1].Value);
            int seasonNumber = int.Parse(seasonFolderMatch.Groups[2].Value);
            Match episodeFileMatch = EpisodeFileRegex().Match(fileName);

            if (episodeFileMatch.Success)
            {
                int episodeNumber = int.Parse(episodeFileMatch.Groups[2].Value);
                string episodeTitle = episodeFileMatch.Groups[3].Value.Trim();
                await UpsertEpisodeAsync(filePath, showTitle, seasonNumber, episodeNumber, episodeTitle, cancellationToken);
                return;
            }
        }

        // Pattern 2: Plex structure: "Show Name/Season 01/S01E01.mkv"
        Match plexSeasonMatch = PlexSeasonFolderRegex().Match(folderName);
        if (plexSeasonMatch.Success)
        {
            string grandparentDir = Path.GetDirectoryName(directory) ?? string.Empty;
            string grandparentName = Path.GetFileName(grandparentDir);
            string showTitle = CleanShowTitle(grandparentName);
            int seasonNumber = int.Parse(plexSeasonMatch.Groups[1].Value);
            Match episodeFileMatch = EpisodeFileRegex().Match(fileName);

            if (episodeFileMatch.Success)
            {
                int episodeNumber = int.Parse(episodeFileMatch.Groups[2].Value);
                string episodeTitle = episodeFileMatch.Groups[3].Value.Trim();
                await UpsertEpisodeAsync(filePath, showTitle, seasonNumber, episodeNumber, episodeTitle, cancellationToken);
                return;
            }
        }

        // Pattern 3: Direct episode filename e.g. "Show Name S01E01 - Title.mkv"
        Match directMatch = DirectEpisodeRegex().Match(fileName);
        if (directMatch.Success)
        {
            string showTitle = CleanShowTitle(directMatch.Groups[1].Value);
            int seasonNumber = int.Parse(directMatch.Groups[2].Value);
            int episodeNumber = int.Parse(directMatch.Groups[3].Value);
            string episodeTitle = directMatch.Groups[4].Value.Trim();
            await UpsertEpisodeAsync(filePath, showTitle, seasonNumber, episodeNumber, episodeTitle, cancellationToken);
            return;
        }

        // Pattern 4: "Show Name/S01E01.mkv"
        Match epMatch = EpisodeFileRegex().Match(fileName);
        if (epMatch.Success)
        {
            string showTitle = CleanShowTitle(folderName);
            int seasonNumber = int.Parse(epMatch.Groups[1].Value);
            int episodeNumber = int.Parse(epMatch.Groups[2].Value);
            string episodeTitle = epMatch.Groups[3].Value.Trim();
            await UpsertEpisodeAsync(filePath, showTitle, seasonNumber, episodeNumber, episodeTitle, cancellationToken);
            return;
        }

        _logger.LogWarning("Could not parse TV episode info from: {FilePath}", filePath);
    }

    private static string CleanShowTitle(string raw)
    {
        string cleaned = Regex.Replace(raw, @"[\(\[](19|20)\d{2}[\)\]]", string.Empty);
        return cleaned.Replace(".", " ").Replace("_", " ").Trim();
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string folderName = Path.GetFileName(directory);
        string fileName = Path.GetFileNameWithoutExtension(filePath);

        if (SeasonFolderRegex().IsMatch(folderName) ||
            PlexSeasonFolderRegex().IsMatch(folderName) ||
            DirectEpisodeRegex().IsMatch(fileName) ||
            EpisodeFileRegex().IsMatch(fileName))
        {
            await ProcessTvFileAsync(filePath, cancellationToken);
        }
        else
        {
            await ProcessMovieFileAsync(filePath, cancellationToken);
        }
    }

    private async Task UpsertMovieAsync(string filePath, string rawFileName, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        MediaItem? existing = await db.MediaItems
            .FirstOrDefaultAsync(m => m.FilePath == filePath, cancellationToken);

        if (existing is not null)
        {
            existing.FileAvailable = true;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        string title = CleanMovieTitle(rawFileName);
        int year = ExtractYear(rawFileName);
        string? subtitlePath = FindSubtitleFile(filePath);

        MediaItem newItem = new()
        {
            FilePath = filePath,
            Title = title,
            ReleaseYear = year,
            ExternalSubtitlePath = subtitlePath,
            FileAvailable = true,
            DateAdded = DateTime.UtcNow
        };

        db.MediaItems.Add(newItem);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added movie: {Title} ({Year})", title, year);

        await _metadataService.EnrichMovieAsync(newItem);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertEpisodeAsync(
        string filePath, string showTitle, int seasonNumber, int episodeNumber, string episodeTitle, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        Episode? existingEpisode = await db.Episodes
            .FirstOrDefaultAsync(e => e.FilePath == filePath, cancellationToken);

        if (existingEpisode is not null)
        {
            existingEpisode.FileAvailable = true;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        TvShow? show = await db.TvShows
            .FirstOrDefaultAsync(t => t.Title == showTitle, cancellationToken);

        if (show is null)
        {
            show = new TvShow
            {
                Title = showTitle,
                DateAdded = DateTime.UtcNow
            };
            db.TvShows.Add(show);
            await db.SaveChangesAsync(cancellationToken);

            await _metadataService.EnrichTvShowAsync(show);
            await db.SaveChangesAsync(cancellationToken);
        }

        Season? season = await db.Seasons
            .FirstOrDefaultAsync(s => s.TvShowId == show.Id && s.SeasonNumber == seasonNumber, cancellationToken);

        if (season is null)
        {
            season = new Season
            {
                TvShowId = show.Id,
                SeasonNumber = seasonNumber,
                Title = $"Season {seasonNumber}",
                DateAdded = DateTime.UtcNow
            };
            db.Seasons.Add(season);
            await db.SaveChangesAsync(cancellationToken);
        }

        string? subtitlePath = FindSubtitleFile(filePath);
        
        string finalTitle = string.IsNullOrWhiteSpace(episodeTitle) ? $"Episode {episodeNumber}" : episodeTitle;

        Episode episode = new()
        {
            SeasonId = season.Id,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            FilePath = filePath,
            Title = finalTitle,
            ExternalSubtitlePath = subtitlePath,
            FileAvailable = true,
            DateAdded = DateTime.UtcNow
        };

        db.Episodes.Add(episode);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Added episode: {Show} S{Season:D2}E{Episode:D2} \"{Title}\"",
            showTitle, seasonNumber, episodeNumber, finalTitle);

        if (show.TmdbId.HasValue)
        {
            await _metadataService.EnrichEpisodeAsync(episode, show.TmdbId.Value);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ReconcileAsync(List<string> foundFiles, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();

        HashSet<string> foundSet = new(foundFiles, StringComparer.OrdinalIgnoreCase);

        List<MediaItem> missingMovies = await db.MediaItems
            .Where(m => m.FileAvailable)
            .ToListAsync(cancellationToken);

        foreach (MediaItem movie in missingMovies.Where(m => !foundSet.Contains(m.FilePath)))
        {
            movie.FileAvailable = false;
            _logger.LogInformation("File no longer found, marking unavailable: {File}", movie.FilePath);
        }

        List<Episode> missingEpisodes = await db.Episodes
            .Where(e => e.FileAvailable)
            .ToListAsync(cancellationToken);

        foreach (Episode ep in missingEpisodes.Where(e => !foundSet.Contains(e.FilePath)))
        {
            ep.FileAvailable = false;
            _logger.LogInformation("Episode file no longer found: {File}", ep.FilePath);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CleanMovieTitle(string rawFileName)
    {
        string cleaned = Regex.Replace(rawFileName, @"[\(\[](19|20)\d{2}[\)\]]", string.Empty);

        cleaned = Regex.Replace(
            cleaned,
            @"\b(1080p|720p|4K|BluRay|BDRip|HDRip|WEBRip|WEB-DL|x264|x265|HEVC|AAC|AC3|DTS)\b.*",
            string.Empty, RegexOptions.IgnoreCase);

        return cleaned.Replace('.', ' ').Replace('_', ' ').Trim();
    }

    private static int ExtractYear(string rawFileName)
    {
        Match match = YearDigitsRegex().Match(rawFileName);
        if (match.Success && int.TryParse(match.Value, out int year))
        {
            return year;
        }
        return 0;
    }

    private static string? FindSubtitleFile(string videoFilePath)
    {
        string? directory = Path.GetDirectoryName(videoFilePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;
            
        string baseName = Path.GetFileNameWithoutExtension(videoFilePath);

        try 
        {
            var files = Directory.EnumerateFiles(directory);
            foreach (var file in files)
            {
                if (Path.GetFileNameWithoutExtension(file).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Constants.AppConstants.SubtitleExtensions.Contains(ext))
                    {
                        return file;
                    }
                }
            }
        }
        catch 
        {
            // Ignore I/O errors here
        }

        return null;
    }
}
