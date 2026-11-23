namespace Couchmark.Services;

/// <summary>
/// Progress information reported by the library scanner
/// to update the UI during a scan operation.
/// </summary>
public record ScanProgress(
    /// <summary>Number of files processed so far.</summary>
    int ProcessedCount,
    /// <summary>Total number of video files found in the library paths.</summary>
    int TotalCount,
    /// <summary>Name of the file currently being processed.</summary>
    string CurrentFileName
);

/// <summary>
/// Scans configured library folders for video files, classifies them as
/// movies or TV show episodes, and upserts records into the database.
///
/// Implements two strategies:
/// 1. Full scan  — used on startup and "Scan Now" button. Checks all files.
/// 2. Reconcile  — called after a FileSystemWatcher event. Only processes
///                 added/removed paths rather than the whole library.
/// </summary>
public interface ILibraryScanner
{
    /// <summary>
    /// Performs a full scan of all configured movie and TV show library paths.
    /// New files are added; missing files are flagged as unavailable.
    /// Progress is reported via <paramref name="progress"/> after each file.
    /// </summary>
    /// <param name="progress">
    /// Optional progress reporter for updating a UI progress bar or status label.
    /// </param>
    /// <param name="cancellationToken">Allows the scan to be cancelled by the user.</param>
    Task ScanAllAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single newly-detected file path (called by the FileSystemWatcher).
    /// Adds it to the database without re-scanning the entire library.
    /// </summary>
    /// <param name="filePath">Absolute path to the new video file.</param>
    Task AddFileAsync(string filePath);

    /// <summary>
    /// Marks a file as unavailable in the database (called when the FileSystemWatcher
    /// detects a deletion). Does not delete the record or watch history.
    /// </summary>
    /// <param name="filePath">Absolute path to the deleted video file.</param>
    Task RemoveFileAsync(string filePath);

    /// <summary>
    /// Marks all files located within a directory as unavailable in the database
    /// (called when the FileSystemWatcher detects a directory deletion).
    /// </summary>
    /// <param name="directoryPath">Absolute path to the deleted directory.</param>
    Task RemoveDirectoryAsync(string directoryPath);
}
