using Couchmark.Constants;
using Couchmark.Data.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using TMDbLib.Client;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.Search;

// Alias to resolve name clash: our entity is also called TvShow
using TmdbTvShow = TMDbLib.Objects.TvShows.TvShow;
using TmdbEpisode = TMDbLib.Objects.TvShows.TvEpisode;

namespace Couchmark.Services;

/// <summary>
/// Fetches and caches metadata for movies and TV shows from TMDB and OMDB.
///
/// TMDB provides: poster, backdrop, overview, genres, cast, IMDB ID, total season count.
/// OMDB provides: IMDB rating (using the IMDB ID from TMDB).
///
/// Downloaded poster images are cached locally in %AppData%\Couchmark\Posters\
/// to avoid re-downloading on every app launch.
/// </summary>
public class MetadataService : IMetadataService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MetadataService> _logger;
    private readonly string _posterCachePath;
    private readonly string _stillCachePath;

    // TMDbLib client — initialised lazily on first use so a missing API key
    // doesn't crash the app at startup.
    private TMDbClient? _tmdbClient;
    private string? _cachedApiKey;
    private readonly object _tmdbClientLock = new object();

    public MetadataService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<MetadataService> logger)
    {
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;

        string appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppName);

        _posterCachePath = Path.Combine(appDataRoot, AppConstants.PosterCacheFolder);
        _stillCachePath  = Path.Combine(appDataRoot, "Stills");

        Directory.CreateDirectory(_posterCachePath);
        Directory.CreateDirectory(_stillCachePath);
    }

    // ── TMDbClient Lazy Init ─────────────────────────────────────────────────

    /// <summary>
    /// Gets or creates the TMDbLib client using the API key from configuration.
    /// Returns null if the API key is not configured yet.
    /// </summary>
    private TMDbClient? GetTmdbClient()
    {
        string? apiKey = _configuration["ApiKeys:Tmdb"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("TMDB API key is not configured. Metadata will not be fetched.");
            return null;
        }

        if (_tmdbClient is not null && _cachedApiKey == apiKey)
        {
            return _tmdbClient;
        }

        lock (_tmdbClientLock)
        {
            apiKey = _configuration["ApiKeys:Tmdb"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _tmdbClient = null;
                _cachedApiKey = null;
                _logger.LogWarning("TMDB API key is not configured. Metadata will not be fetched.");
                return null;
            }

            if (_tmdbClient is null || _cachedApiKey != apiKey)
            {
                _tmdbClient = new TMDbClient(apiKey);
                _cachedApiKey = apiKey;
            }

            return _tmdbClient;
        }
    }

    // ── Search ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<int?> SearchMovieAsync(string title, int? year = null)
    {
        TMDbClient? client = GetTmdbClient();
        if (client is null) return null;

        try
        {
            TMDbLib.Objects.General.SearchContainer<SearchMovie> results =
                await client.SearchMovieAsync(title, year: year.GetValueOrDefault(0));

            SearchMovie? best = results.Results.FirstOrDefault();
            return best?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMDB movie search failed for title: {Title}", title);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<int?> SearchTvShowAsync(string title)
    {
        TMDbClient? client = GetTmdbClient();
        if (client is null) return null;

        try
        {
            TMDbLib.Objects.General.SearchContainer<SearchTv> results =
                await client.SearchTvShowAsync(title);

            SearchTv? best = results.Results.FirstOrDefault();
            return best?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TMDB TV search failed for title: {Title}", title);
            return null;
        }
    }

    // ── Enrichment ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task EnrichMovieAsync(MediaItem item)
    {
        TMDbClient? client = GetTmdbClient();
        if (client is null) return;

        try
        {
            // Search TMDB if we don't have an ID yet
            if (!item.TmdbId.HasValue)
            {
                item.TmdbId = await SearchMovieAsync(item.Title, item.ReleaseYear > 0 ? item.ReleaseYear : null);
            }

            if (!item.TmdbId.HasValue)
            {
                _logger.LogWarning("No TMDB match found for movie: {Title}", item.Title);
                return;
            }

            // Fetch full movie details from TMDB
            Movie movie = await client.GetMovieAsync(item.TmdbId.Value);

            item.Title = movie.Title ?? item.Title;
            item.Overview = movie.Overview ?? string.Empty;
            item.PosterPath = movie.PosterPath;
            item.BackdropPath = movie.BackdropPath;
            item.ReleaseYear = movie.ReleaseDate?.Year ?? item.ReleaseYear;
            item.RuntimeMinutes = movie.Runtime ?? 0;
            item.TmdbRating = movie.VoteAverage;
            item.ImdbId = movie.ImdbId;
            item.Genres = string.Join(",", movie.Genres?.Select(g => g.Name) ?? []);

            // Fetch IMDB rating from OMDB
            if (!string.IsNullOrEmpty(item.ImdbId))
            {
                item.ImdbRating = await FetchImdbRatingAsync(item.ImdbId);
            }

            // Download and cache the poster image
            if (!string.IsNullOrEmpty(item.PosterPath))
            {
                string fileName = $"movie_{item.TmdbId}.jpg";
                item.LocalPosterPath = await DownloadPosterAsync(item.PosterPath, fileName);
            }

            _logger.LogInformation("Enriched movie: {Title} (TMDB: {Id})", item.Title, item.TmdbId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enrich movie metadata for: {Title}", item.Title);
        }
    }

    public async Task EnrichTvShowAsync(TvShow show)
    {
        TMDbClient? client = GetTmdbClient();
        if (client != null)
        {
            try
            {
                if (!show.TmdbId.HasValue) show.TmdbId = await SearchTvShowAsync(show.Title);

                if (show.TmdbId.HasValue)
                {
                    TmdbTvShow tmdbShow = await client.GetTvShowAsync(show.TmdbId.Value, TMDbLib.Objects.TvShows.TvShowMethods.ExternalIds);

                    show.Title = tmdbShow.Name ?? show.Title;
                    show.Overview = tmdbShow.Overview ?? string.Empty;
                    show.PosterPath = tmdbShow.PosterPath;
                    show.BackdropPath = tmdbShow.BackdropPath;
                    show.FirstAirYear = tmdbShow.FirstAirDate?.Year ?? 0;
                    show.TmdbRating = tmdbShow.VoteAverage;
                    show.TotalSeasonsOnTmdb = tmdbShow.NumberOfSeasons;
                    show.Genres = string.Join(",", tmdbShow.Genres?.Select(g => g.Name) ?? []);
                    show.ImdbId = tmdbShow.ExternalIds?.ImdbId;

                    if (!string.IsNullOrEmpty(show.ImdbId))
                    {
                        show.ImdbRating = await FetchImdbRatingAsync(show.ImdbId);
                    }

                    if (!string.IsNullOrEmpty(show.PosterPath))
                    {
                        string fileName = $"show_{show.TmdbId}.jpg";
                        show.LocalPosterPath = await DownloadPosterAsync(show.PosterPath, fileName);
                    }

                    _logger.LogInformation("Enriched TV show with TMDB: {Title}", show.Title);
                    return; // Success, exit early
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TMDB enrichment failed for: {Title}", show.Title);
            }
        }

        // ── OMDB FALLBACK ──
        string? omdbKey = _configuration["ApiKeys:Omdb"];
        if (string.IsNullOrWhiteSpace(omdbKey))
        {
            _logger.LogWarning("Neither TMDB nor OMDB keys are configured. Cannot enrich {Title}.", show.Title);
            return;
        }

        try
        {
            _logger.LogInformation("Falling back to OMDB for TV Show: {Title}", show.Title);
            string encodedTitle = Uri.EscapeDataString(show.Title);
            string url = $"{AppConstants.OmdbBaseUrl}?t={encodedTitle}&type=series&apikey={omdbKey}";
            string json = await _httpClient.GetStringAsync(url);
            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Response", out JsonElement responseEl) && responseEl.GetString() == "True")
            {
                if (doc.RootElement.TryGetProperty("Plot", out JsonElement plotEl))
                    show.Overview = plotEl.GetString() ?? show.Overview;

                if (doc.RootElement.TryGetProperty("imdbRating", out JsonElement ratingEl))
                    show.ImdbRating = ratingEl.GetString() ?? string.Empty;

                if (doc.RootElement.TryGetProperty("imdbID", out JsonElement imdbIdEl))
                    show.ImdbId = imdbIdEl.GetString();

                if (doc.RootElement.TryGetProperty("Poster", out JsonElement posterEl))
                {
                    string posterUrl = posterEl.GetString() ?? string.Empty;
                    if (posterUrl.StartsWith("http"))
                    {
                        show.PosterPath = posterUrl;
                        string fileName = $"omdb_show_{Uri.EscapeDataString(show.Title)}.jpg";
                        
                        // OMDB provides full URLs, so we need a slightly modified download logic 
                        // but we can just download the raw bytes manually here since DownloadPosterAsync prepends TMDB base URL.
                        string localPath = Path.Combine(_posterCachePath, fileName);
                        if (!File.Exists(localPath))
                        {
                            byte[] imageBytes = await _httpClient.GetByteArrayAsync(posterUrl);
                            await File.WriteAllBytesAsync(localPath, imageBytes);
                        }
                        show.LocalPosterPath = localPath;
                    }
                }
                
                _logger.LogInformation("Enriched TV show with OMDB: {Title}", show.Title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OMDB fallback enrichment failed for: {Title}", show.Title);
        }
    }

    /// <inheritdoc/>
    public async Task EnrichEpisodeAsync(Episode episode, int tmdbShowId)
    {
        TMDbClient? client = GetTmdbClient();
        if (client is null) return;

        try
        {
            TmdbEpisode? ep = await client.GetTvEpisodeAsync(
                tmdbShowId, episode.SeasonNumber, episode.EpisodeNumber);

            if (ep is null) return;

            episode.TmdbId = ep.Id;
            episode.Title = ep.Name ?? episode.Title;
            episode.Overview = ep.Overview ?? string.Empty;
            episode.AirDate = ep.AirDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            episode.RuntimeMinutes = ep.Runtime ?? 0;

            // Download episode still thumbnail if TMDB has one
            if (!string.IsNullOrEmpty(ep.StillPath))
            {
                string? stillLocalPath = await DownloadStillAsync(ep.StillPath, $"ep_{episode.Id}.jpg");
                if (stillLocalPath is not null)
                {
                    episode.StillPath = stillLocalPath;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to enrich episode metadata: S{Season:D2}E{Episode:D2}",
                episode.SeasonNumber, episode.EpisodeNumber);
        }
    }

    // ── OMDB Rating ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the IMDB rating string from the OMDB API for a given IMDB ID.
    /// Returns "N/A" if the rating is unavailable or the request fails.
    /// </summary>
    private async Task<string> FetchImdbRatingAsync(string imdbId)
    {
        string? omdbKey = _configuration["ApiKeys:Omdb"];
        if (string.IsNullOrWhiteSpace(omdbKey))
        {
            return string.Empty;
        }

        try
        {
            string url = $"{AppConstants.OmdbBaseUrl}?i={imdbId}&apikey={omdbKey}";
            string json = await _httpClient.GetStringAsync(url);

            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("imdbRating", out JsonElement ratingEl))
            {
                return ratingEl.GetString() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OMDB rating fetch failed for IMDB ID: {ImdbId}", imdbId);
        }

        return string.Empty;
    }

    // ── Poster Download ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string?> DownloadPosterAsync(string posterPath, string fileName)
    {
        try
        {
            string localPath = Path.Combine(_posterCachePath, fileName);

            // Skip download if the image is already cached
            if (File.Exists(localPath))
            {
                return localPath;
            }

            string url = $"{AppConstants.TmdbImageBaseUrl}{AppConstants.TmdbPosterSizeCard}{posterPath}";
            byte[] imageBytes = await _httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, imageBytes);

            return localPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download poster: {PosterPath}", posterPath);
            return null;
        }
    }

    /// <summary>
    /// Downloads and caches a TMDB episode still image (w300) to the local Stills folder.
    /// Returns the local file path on success, or null if the download fails.
    /// </summary>
    private async Task<string?> DownloadStillAsync(string stillPath, string fileName)
    {
        try
        {
            string localPath = Path.Combine(_stillCachePath, fileName);

            // Skip download if already cached
            if (File.Exists(localPath))
            {
                return localPath;
            }

            // Use w300 still size — perfect fit for the 160×90 thumbnail cards
            string url = $"{AppConstants.TmdbImageBaseUrl}w300{stillPath}";
            byte[] imageBytes = await _httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(localPath, imageBytes);

            return localPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download episode still: {StillPath}", stillPath);
            return null;
        }
    }

    public void Dispose()
    {
        (_tmdbClient as IDisposable)?.Dispose();
    }
}
