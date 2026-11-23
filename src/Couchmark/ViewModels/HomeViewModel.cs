using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Couchmark.ViewModels;

public class HomeMediaCard
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string PosterPath { get; set; } = string.Empty;
    public bool IsMovie { get; set; }
    public bool IsEpisode { get; set; }
    
    // Progress info
    public double ProgressPercent { get; set; }
    public bool ShowProgress => ProgressPercent > 0 && ProgressPercent < 100;
}

public partial class HomeViewModel : ObservableObject, Wpf.Ui.Controls.INavigationAware
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Couchmark.Services.ICouchmarkNavigationService _navigationService;
    private readonly IConfiguration _configuration;
    private readonly Couchmark.Services.IPlaybackTracker _playbackTracker;
    private readonly Wpf.Ui.ISnackbarService _snackbarService;

    public HomeViewModel(
        IServiceScopeFactory scopeFactory,
        Couchmark.Services.ICouchmarkNavigationService navigationService,
        IConfiguration configuration,
        Couchmark.Services.IPlaybackTracker playbackTracker,
        Wpf.Ui.ISnackbarService snackbarService)
    {
        _scopeFactory = scopeFactory;
        _navigationService = navigationService;
        _configuration = configuration;
        _playbackTracker = playbackTracker;
        _snackbarService = snackbarService;
    }

    [ObservableProperty]
    private string _heroTitle = "Welcome to Couchmark";

    [ObservableProperty]
    private string _heroSubtitle = "Jump right back into your shows.";

    [ObservableProperty]
    private bool _showContinueWatching = true;

    [ObservableProperty]
    private bool _showRecentlyAddedShows = true;

    [ObservableProperty]
    private bool _showRecentlyAddedMovies = true;

    [ObservableProperty]
    private bool _isContinueWatchingFirst = true;

    public ObservableCollection<HomeMediaCard> ContinueWatchingItems { get; } = new();
    public ObservableCollection<HomeMediaCard> RecentlyAddedShows { get; } = new();
    public ObservableCollection<HomeMediaCard> RecentlyAddedMovies { get; } = new();

    public void OnNavigatedTo()
    {
        LoadPreferences();
        _ = LoadDataAsync();
    }

    public void OnNavigatedFrom() { }

    private void LoadPreferences()
    {
        try
        {
            // Read directly from appsettings.local.json to get instant changes from Settings
            string appDataPath = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                Constants.AppConstants.AppName);
            string localSettingsPath = Path.Combine(appDataPath, "appsettings.local.json");

            if (File.Exists(localSettingsPath))
            {
                string json = File.ReadAllText(localSettingsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("HomeRows", out var homeRows))
                {
                    if (homeRows.TryGetProperty("ShowContinueWatching", out var scw))
                        ShowContinueWatching = scw.GetBoolean();
                    if (homeRows.TryGetProperty("ShowRecentlyAddedShows", out var srs))
                        ShowRecentlyAddedShows = srs.GetBoolean();
                    if (homeRows.TryGetProperty("ShowRecentlyAddedMovies", out var srm))
                        ShowRecentlyAddedMovies = srm.GetBoolean();
                    if (homeRows.TryGetProperty("RowOrder", out var ro))
                        IsContinueWatchingFirst = ro.GetString() == "Continue Watching First";
                    return;
                }
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not load HomeRows settings from local json, using defaults.");
        }

        // Fallback to IConfiguration
        ShowContinueWatching = _configuration.GetValue<bool?>("HomeRows:ShowContinueWatching") ?? true;
        ShowRecentlyAddedShows = _configuration.GetValue<bool?>("HomeRows:ShowRecentlyAddedShows") ?? true;
        ShowRecentlyAddedMovies = _configuration.GetValue<bool?>("HomeRows:ShowRecentlyAddedMovies") ?? true;
        IsContinueWatchingFirst = (_configuration["HomeRows:RowOrder"] ?? "Continue Watching First") == "Continue Watching First";
    }

    private async Task LoadDataAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

        // 1. Load Continue Watching
        if (ShowContinueWatching)
        {
            var progressItems = await dbContext.WatchProgress
                .Include(w => w.Episode).ThenInclude(e => e!.Season).ThenInclude(s => s!.TvShow)
                .Include(w => w.MediaItem)
                .Where(w => !w.IsCompleted && w.PositionSeconds > 0)
                .OrderByDescending(w => w.LastWatchedAt)
                .Take(12)
                .ToListAsync();

            ContinueWatchingItems.Clear();
            foreach (var p in progressItems)
            {
                if (p.Episode != null && p.Episode.Season?.TvShow != null)
                {
                    ContinueWatchingItems.Add(new HomeMediaCard
                    {
                        Id = p.Episode.Id,
                        IsMovie = false,
                        IsEpisode = true,
                        Title = p.Episode.Season.TvShow.Title,
                        Subtitle = $"S{p.Episode.SeasonNumber:D2}E{p.Episode.EpisodeNumber:D2} - {p.Episode.Title}",
                        PosterPath = p.Episode.Season.TvShow.LocalPosterPath ?? string.Empty,
                        ProgressPercent = p.DurationSeconds > 0 ? (p.PositionSeconds * 100.0) / p.DurationSeconds : 0
                    });
                }
                else if (p.MediaItem != null)
                {
                    ContinueWatchingItems.Add(new HomeMediaCard
                    {
                        Id = p.MediaItem.Id,
                        IsMovie = true,
                        IsEpisode = false,
                        Title = p.MediaItem.Title,
                        Subtitle = "Movie",
                        PosterPath = p.MediaItem.LocalPosterPath ?? string.Empty,
                        ProgressPercent = p.DurationSeconds > 0 ? (p.PositionSeconds * 100.0) / p.DurationSeconds : 0
                    });
                }
            }
        }

        // 2. Load Recently Added TV Shows
        if (ShowRecentlyAddedShows)
        {
            var recentShows = await dbContext.TvShows
                .OrderByDescending(t => t.DateAdded)
                .Take(12)
                .ToListAsync();

            RecentlyAddedShows.Clear();
            foreach (var show in recentShows)
            {
                RecentlyAddedShows.Add(new HomeMediaCard
                {
                    Id = show.Id,
                    IsMovie = false,
                    IsEpisode = false,
                    Title = show.Title,
                    Subtitle = show.FirstAirYear > 0 ? show.FirstAirYear.ToString() : "Series",
                    PosterPath = show.LocalPosterPath ?? string.Empty
                });
            }
        }

        // 3. Load Recently Added Movies
        if (ShowRecentlyAddedMovies)
        {
            var recentMovies = await dbContext.MediaItems
                .OrderByDescending(m => m.DateAdded)
                .Take(12)
                .ToListAsync();

            RecentlyAddedMovies.Clear();
            foreach (var movie in recentMovies)
            {
                RecentlyAddedMovies.Add(new HomeMediaCard
                {
                    Id = movie.Id,
                    IsMovie = true,
                    IsEpisode = false,
                    Title = movie.Title,
                    Subtitle = movie.ReleaseYear > 0 ? movie.ReleaseYear.ToString() : "Movie",
                    PosterPath = movie.LocalPosterPath ?? string.Empty
                });
            }
        }
    }

    [RelayCommand]
    private void CardClicked(HomeMediaCard card)
    {
        if (card == null) return;

        if (card.IsMovie)
        {
            if (card.ProgressPercent > 0)
            {
                _navigationService.NavigateTo<Views.PlayerView>(new Services.PlayerNavigationArgs(card.Id, IsMovie: true));
            }
            else
            {
                _navigationService.NavigateTo<Views.DetailView>(new Services.PlayerNavigationArgs(card.Id, IsMovie: true));
            }
        }
        else
        {
            // If it's an episode in "Continue Watching", play it directly
            if (card.IsEpisode || card.ProgressPercent > 0)
            {
                _navigationService.NavigateTo<Views.PlayerView>(new Services.PlayerNavigationArgs(card.Id, IsMovie: false));
            }
            else
            {
                // Recently added TV show -> go to DetailView
                _navigationService.NavigateTo<Views.DetailView>(card.Id);
            }
        }
    }

    [RelayCommand]
    private void ViewMovieDetails(HomeMediaCard? card)
    {
        if (card == null) return;
        _navigationService.NavigateTo<Views.DetailView>(new Services.PlayerNavigationArgs(card.Id, IsMovie: true));
    }

    [RelayCommand]
    private void PlayMovie(HomeMediaCard? card)
    {
        if (card == null) return;
        _navigationService.NavigateTo<Views.PlayerView>(new Services.PlayerNavigationArgs(card.Id, IsMovie: true));
    }

    [RelayCommand]
    private async Task OpenFileLocationAsync(HomeMediaCard? card)
    {
        if (card == null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            if (card.IsMovie)
            {
                var movie = await dbContext.MediaItems.FirstOrDefaultAsync(m => m.Id == card.Id);
                if (movie != null && !string.IsNullOrEmpty(movie.FilePath) && File.Exists(movie.FilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{movie.FilePath}\"",
                        UseShellExecute = true
                    });
                }
            }
            else if (card.IsEpisode)
            {
                var episode = await dbContext.Episodes.FirstOrDefaultAsync(e => e.Id == card.Id);
                if (episode != null && !string.IsNullOrEmpty(episode.FilePath) && File.Exists(episode.FilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{episode.FilePath}\"",
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                // TV Show: find the folder of its first available episode
                var ep = await dbContext.Episodes
                    .Include(e => e.Season)
                    .Where(e => e.Season.TvShowId == card.Id && !string.IsNullOrEmpty(e.FilePath))
                    .FirstOrDefaultAsync();

                if (ep != null && File.Exists(ep.FilePath))
                {
                    string? folder = Path.GetDirectoryName(ep.FilePath);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{folder}\"",
                            UseShellExecute = true
                        });
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to open explorer location for home card");
        }
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync(HomeMediaCard? card)
    {
        if (card == null) return;

        try
        {
            if (card.IsMovie)
            {
                var progress = await _playbackTracker.GetProgressAsync(card.Id, null);
                bool currentWatched = progress?.IsCompleted ?? false;
                bool newWatched = !currentWatched;

                await _playbackTracker.SetWatchedAsync(card.Id, null, newWatched);
                await LoadDataAsync();

                _snackbarService.Show(
                    newWatched ? "Marked as Watched" : "Marked as Unwatched",
                    $"{(newWatched ? "Marked" : "Unmarked")} {card.Title} as watched.",
                    Wpf.Ui.Controls.ControlAppearance.Success,
                    new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                    TimeSpan.FromSeconds(2));
            }
            else if (card.IsEpisode)
            {
                var progress = await _playbackTracker.GetProgressAsync(null, card.Id);
                bool currentWatched = progress?.IsCompleted ?? false;
                bool newWatched = !currentWatched;

                await _playbackTracker.SetWatchedAsync(null, card.Id, newWatched);
                await LoadDataAsync();

                _snackbarService.Show(
                    newWatched ? "Marked as Watched" : "Marked as Unwatched",
                    $"{(newWatched ? "Marked" : "Unmarked")} {card.Title} as watched.",
                    Wpf.Ui.Controls.ControlAppearance.Success,
                    new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                    TimeSpan.FromSeconds(2));
            }
            else
            {
                // Full TV Show
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

                var fullShow = await dbContext.TvShows
                    .Include(t => t.Seasons)
                    .ThenInclude(s => s.Episodes)
                    .ThenInclude(e => e.WatchProgress)
                    .FirstOrDefaultAsync(t => t.Id == card.Id);

                if (fullShow != null)
                {
                    bool anyUnwatched = fullShow.Seasons.SelectMany(s => s.Episodes)
                        .Any(e => e.WatchProgress == null || !e.WatchProgress.IsCompleted);

                    bool newWatchedStatus = anyUnwatched;

                    foreach (var season in fullShow.Seasons)
                    {
                        foreach (var ep in season.Episodes)
                        {
                            await _playbackTracker.SetWatchedAsync(null, ep.Id, newWatchedStatus);
                        }
                    }

                    await LoadDataAsync();

                    _snackbarService.Show(
                        newWatchedStatus ? "Marked All Watched" : "Marked All Unwatched",
                        $"{(newWatchedStatus ? "Marked all" : "Unmarked all")} episodes of {card.Title}.",
                        Wpf.Ui.Controls.ControlAppearance.Success,
                        new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                        TimeSpan.FromSeconds(2));
                }
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to toggle watched status for home card");
        }
    }
}
