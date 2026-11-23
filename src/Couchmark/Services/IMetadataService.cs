using Couchmark.Data.Entities;

namespace Couchmark.Services;

/// <summary>
/// Fetches and caches rich metadata for movies and TV shows from
/// TMDB (The Movie Database) and OMDB (IMDB rating wrapper).
///
/// Metadata is stored in the SQLite database after first fetch.
/// Poster images are downloaded to %AppData%\Couchmark\Posters\ and
/// referenced by LocalPosterPath on the entity.
/// </summary>
public interface IMetadataService
{
    /// <summary>
    /// Searches TMDB for a movie by title and returns the best matching TMDB ID.
    /// Returns null if no match is found.
    /// </summary>
    /// <param name="title">Movie title extracted from the filename.</param>
    /// <param name="year">Optional release year to narrow the search.</param>
    Task<int?> SearchMovieAsync(string title, int? year = null);

    /// <summary>
    /// Searches TMDB for a TV show by title and returns the best matching TMDB ID.
    /// Returns null if no match is found.
    /// </summary>
    /// <param name="title">Show title extracted from the folder name.</param>
    Task<int?> SearchTvShowAsync(string title);

    /// <summary>
    /// Fetches full movie metadata from TMDB and enriches the given MediaItem entity.
    /// Also fetches the IMDB rating from OMDB using the ImdbId returned by TMDB.
    /// </summary>
    /// <param name="item">The MediaItem to enrich. Modified in place.</param>
    Task EnrichMovieAsync(MediaItem item);

    /// <summary>
    /// Fetches full TV show metadata from TMDB and enriches the given TvShow entity.
    /// Fetches the total season count from TMDB (for the hybrid display feature).
    /// Also fetches the IMDB rating from OMDB.
    /// </summary>
    /// <param name="show">The TvShow entity to enrich. Modified in place.</param>
    Task EnrichTvShowAsync(TvShow show);

    /// <summary>
    /// Fetches metadata for a single episode from TMDB and enriches the given Episode entity.
    /// </summary>
    /// <param name="episode">The Episode entity to enrich. Modified in place.</param>
    /// <param name="tmdbShowId">The TMDB ID of the parent show.</param>
    Task EnrichEpisodeAsync(Episode episode, int tmdbShowId);

    /// <summary>
    /// Downloads a poster image from TMDB to the local poster cache folder.
    /// Returns the local file path where the image was saved.
    /// Returns null if the download fails (e.g. no internet connection).
    /// </summary>
    /// <param name="posterPath">
    /// The relative poster path from TMDB (e.g. "/abc123.jpg").
    /// </param>
    /// <param name="fileName">
    /// The filename to save as in the cache folder (e.g. "movie_550.jpg").
    /// </param>
    Task<string?> DownloadPosterAsync(string posterPath, string fileName);
}
