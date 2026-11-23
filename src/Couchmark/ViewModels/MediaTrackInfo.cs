namespace Couchmark.ViewModels;

/// <summary>
/// Represents an audio or subtitle track discovered in the media file.
/// </summary>
/// <param name="Id">The LibVLC track identifier.</param>
/// <param name="Name">Human-readable description/language name of the track.</param>
public record MediaTrackInfo(int Id, string Name);
