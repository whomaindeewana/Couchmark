using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Couchmark.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using System.Collections.ObjectModel;

namespace Couchmark.ViewModels;

/// <summary>
/// ViewModel for SettingsView.
/// Manages API key entry, library folder paths, appearance settings,
/// and the "Scan Now" trigger.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfiguration _configuration;
    private readonly ILibraryScanner _libraryScanner;
    private bool _isInitialized;

    /// <summary>Initialises the ViewModel and loads saved settings.</summary>
    public SettingsViewModel(IConfiguration configuration, ILibraryScanner libraryScanner)
    {
        _configuration = configuration;
        _libraryScanner = libraryScanner;

        // Load saved values from appsettings.json into the bound properties
        TmdbApiKey = _configuration["ApiKeys:Tmdb"] ?? string.Empty;
        OmdbApiKey = _configuration["ApiKeys:Omdb"] ?? string.Empty;

        // Load saved library paths
        string[] moviePaths = _configuration.GetSection("Libraries:MoviePaths").Get<string[]>() ?? [];
        string[] tvPaths = _configuration.GetSection("Libraries:TvShowPaths").Get<string[]>() ?? [];

        foreach (string p in moviePaths) MoviePaths.Add(p);
        foreach (string p in tvPaths) TvShowPaths.Add(p);

        string savedTheme = _configuration["Appearance:Theme"] ?? "Midnight";
        SelectedTheme = savedTheme;
        Themes.ThemeHelper.ApplyTheme(savedTheme);

        // Load Home screen customizations
        ShowContinueWatching = _configuration.GetValue<bool?>("HomeRows:ShowContinueWatching") ?? true;
        ShowRecentlyAddedShows = _configuration.GetValue<bool?>("HomeRows:ShowRecentlyAddedShows") ?? true;
        ShowRecentlyAddedMovies = _configuration.GetValue<bool?>("HomeRows:ShowRecentlyAddedMovies") ?? true;
        SelectedHomeRowOrder = _configuration["HomeRows:RowOrder"] ?? "Continue Watching First";

        _isInitialized = true;
    }

    // ── API Keys ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The user's TMDB Read Access Token or API v3 Key.
    /// Bound to a PasswordBox in SettingsView.
    /// </summary>
    [ObservableProperty]
    private string _tmdbApiKey = string.Empty;

    /// <summary>The user's OMDB API key (for IMDB ratings).</summary>
    [ObservableProperty]
    private string _omdbApiKey = string.Empty;

    // ── Library Paths ─────────────────────────────────────────────────────────

    /// <summary>Folder paths configured as Movie libraries.</summary>
    public ObservableCollection<string> MoviePaths { get; } = [];

    /// <summary>Folder paths configured as TV Show libraries.</summary>
    public ObservableCollection<string> TvShowPaths { get; } = [];

    // ── Status ────────────────────────────────────────────────────────────────

    /// <summary>Status message shown at the bottom of the Settings page.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ── Appearance ────────────────────────────────────────────────────────
    
    [ObservableProperty]
    private string _selectedTheme = "Midnight";

    partial void OnSelectedThemeChanged(string value)
    {
        Themes.ThemeHelper.ApplyTheme(value);
        Save();
    }
    
    public ObservableCollection<string> AvailableThemes { get; } = new(new[] { "White", "Nude", "Midnight", "Vanta Black" });

    // ── Home Screen Customization ─────────────────────────────────────────

    [ObservableProperty]
    private bool _showContinueWatching = true;

    [ObservableProperty]
    private bool _showRecentlyAddedShows = true;

    [ObservableProperty]
    private bool _showRecentlyAddedMovies = true;

    [ObservableProperty]
    private string _selectedHomeRowOrder = "Continue Watching First";

    public ObservableCollection<string> AvailableHomeRowOrders { get; } = new(new[] { "Continue Watching First", "Recently Added First" });

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanNowCommand))]
    private bool _isScanning;

    /// <summary>Scan progress (0–100) for the progress bar.</summary>
    [ObservableProperty]
    private int _scanProgress;

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Opens a folder picker and adds the selected path to the Movie library list.</summary>
    [RelayCommand]
    private void AddMovieFolder()
    {
        OpenFolderDialog dialog = new() { Title = "Select Movie Library Folder" };
        if (dialog.ShowDialog() == true)
        {
            MoviePaths.Add(dialog.FolderName);
            StatusMessage = $"Added: {dialog.FolderName}";
        }
    }

    /// <summary>Opens a folder picker and adds the selected path to the TV Show library list.</summary>
    [RelayCommand]
    private void AddTvShowFolder()
    {
        OpenFolderDialog dialog = new() { Title = "Select TV Show Library Folder" };
        if (dialog.ShowDialog() == true)
        {
            TvShowPaths.Add(dialog.FolderName);
            StatusMessage = $"Added: {dialog.FolderName}";
        }
    }

    /// <summary>Removes the given path from the Movie library list and persists the change.</summary>
    [RelayCommand]
    private void RemoveMovieFolder(string path)
    {
        if (MoviePaths.Remove(path))
        {
            StatusMessage = $"Removed: {path}";
            Save();
        }
    }

    /// <summary>Removes the given path from the TV Show library list and persists the change.</summary>
    [RelayCommand]
    private void RemoveTvShowFolder(string path)
    {
        if (TvShowPaths.Remove(path))
        {
            StatusMessage = $"Removed: {path}";
            Save();
        }
    }

    private bool CanScan() => !IsScanning;

    /// <summary>
    /// Triggers a full library scan across all configured paths.
    /// Runs on a background thread so the UI stays responsive.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanNowAsync()
    {
        IsScanning = true;
        ScanProgress = 0;
        StatusMessage = "Scanning library...";

        // Report progress back to the UI thread
        Progress<ScanProgress> progress = new(p =>
        {
            ScanProgress = (int)((double)p.ProcessedCount / p.TotalCount * 100);
            StatusMessage = $"Scanning: {p.CurrentFileName} ({p.ProcessedCount}/{p.TotalCount})";
        });

        await _libraryScanner.ScanAllAsync(progress);

        IsScanning = false;
        ScanProgress = 100;
        StatusMessage = "Scan complete.";
    }

    /// <summary>
    /// Saves API keys and library paths to appsettings.local.json.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (!_isInitialized) return;

        try
        {
            var configData = new
            {
                ApiKeys = new
                {
                    Tmdb = TmdbApiKey,
                    Omdb = OmdbApiKey
                },
                Libraries = new
                {
                    MoviePaths = MoviePaths.ToArray(),
                    TvShowPaths = TvShowPaths.ToArray()
                },
                Appearance = new
                {
                    Theme = SelectedTheme
                },
                HomeRows = new
                {
                    ShowContinueWatching = ShowContinueWatching,
                    ShowRecentlyAddedShows = ShowRecentlyAddedShows,
                    ShowRecentlyAddedMovies = ShowRecentlyAddedMovies,
                    RowOrder = SelectedHomeRowOrder
                }
            };

            string json = System.Text.Json.JsonSerializer.Serialize(configData, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            string appDataPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                Constants.AppConstants.AppName);
                
            System.IO.Directory.CreateDirectory(appDataPath);
            string path = System.IO.Path.Combine(appDataPath, "appsettings.local.json");
            System.IO.File.WriteAllText(path, json);

            StatusMessage = "Settings saved successfully! You can scan your library now.";
        }
        catch (System.Exception ex)
        {
            StatusMessage = $"Error saving settings: {ex.Message}";
        }
    }
}

