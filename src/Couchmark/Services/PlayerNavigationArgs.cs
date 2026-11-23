namespace Couchmark.Services;

/// <summary>
/// Navigation payload passed to PlayerView to distinguish between movies and TV episodes.
/// </summary>
/// <param name="Id">The database ID (Episode.Id or MediaItem.Id).</param>
/// <param name="IsMovie">True if playing a movie (MediaItem); false if playing a TV show episode.</param>
public record PlayerNavigationArgs(long Id, bool IsMovie);
