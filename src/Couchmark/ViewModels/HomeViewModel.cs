using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Couchmark.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Couchmark.ViewModels;

public partial class HomeMediaCard : ObservableObject
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string PosterPath { get; set; } = string.Empty;
    public bool IsMovie { get; set; }
    public bool IsEpisode { get; set; }
    public bool IsFromContinueWatching { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private double _progressPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private bool _isWatched;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnwatched))]
    private int _unwatchedCount;

    public bool HasUnwatched => UnwatchedCount > 0;
    public bool ShowProgress => !IsWatched && ProgressPercent > 0 && ProgressPercent < 100;
}

public partial class HomeViewModel : ObservableObject, Wpf.Ui.Controls.INavigationAware
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Couchmark.Services.ICouchmarkNavigationService _navigationService;
    private readonly IConfiguration _configuration;
    private readonly Couchmark.Services.IPlaybackTracker _playbackTracker;
    private readonly Wpf.Ui.ISnackbarService _snackbarService;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    private CancellationTokenSource? _loadCts;

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

    [ObservableProperty]
    private bool _isLibraryEmpty;

    [ObservableProperty]
    private bool _isAllRowsEmptyOrHidden;

    public ObservableCollection<HomeMediaCard> ContinueWatchingItems { get; } = new();
    public ObservableCollection<HomeMediaCard> RecentlyAddedShows { get; } = new();
    public ObservableCollection<HomeMediaCard> RecentlyAddedMovies { get; } = new();

    public void OnNavigatedTo()
    {
        LoadPreferences();
        LoadDataAsync().FireAndForgetSafeAsync();
    }

    public void OnNavigatedFrom()
    {
        _loadCts?.Cancel();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.NavigateTo<Views.SettingsView>();
    }

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
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

        await _loadSemaphore.WaitAsync(token);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            bool hasAnyShows = await dbContext.TvShows
                .AsNoTracking()
                .AnyAsync(t => t.Seasons.Any(s => s.Episodes.Any(e => e.FileAvailable)), token);
            bool hasAnyMovies = await dbContext.MediaItems
                .AsNoTracking()
                .AnyAsync(m => m.FileAvailable, token);
            bool libraryEmpty = !hasAnyShows && !hasAnyMovies;

            List<HomeMediaCard> continueWatchingList = [];
            List<HomeMediaCard> recentShowsList = [];
            List<HomeMediaCard> recentMoviesList = [];

            // 1. Load Continue Watching
            if (ShowContinueWatching)
            {
                var progressItems = await dbContext.WatchProgress
                    .AsNoTracking()
                    .Include(w => w.Episode).ThenInclude(e => e!.Season).ThenInclude(s => s!.TvShow)
                    .Include(w => w.MediaItem)
                    .Where(w => !w.IsCompleted && w.PositionSeconds > 0 &&
                                ((w.Episode != null && w.Episode.FileAvailable) ||
                                 (w.MediaItem != null && w.MediaItem.FileAvailable)))
                    .OrderByDescending(w => w.LastWatchedAt)
                    .Take(12)
                    .ToListAsync(token);

                foreach (var p in progressItems)
                {
                    if (p.Episode != null && p.Episode.Season?.TvShow != null)
                    {
                        continueWatchingList.Add(new HomeMediaCard
                        {
                            Id = p.Episode.Id,
                            IsMovie = false,
                            IsEpisode = true,
                            IsFromContinueWatching = true,
                            Title = p.Episode.Season.TvShow.Title,
                            Subtitle = $"S{p.Episode.SeasonNumber:D2}E{p.Episode.EpisodeNumber:D2} - {p.Episode.Title}",
                            PosterPath = p.Episode.Season.TvShow.LocalPosterPath ?? string.Empty,
                            ProgressPercent = p.DurationSeconds > 0 ? (p.PositionSeconds * 100.0) / p.DurationSeconds : 0
                        });
                    }
                    else if (p.MediaItem != null)
                    {
                        continueWatchingList.Add(new HomeMediaCard
                        {
                            Id = p.MediaItem.Id,
                            IsMovie = true,
                            IsEpisode = false,
                            IsFromContinueWatching = true,
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
                    .AsNoTracking()
                    .Where(t => t.Seasons.Any(s => s.Episodes.Any(e => e.FileAvailable)))
                    .Include(t => t.Seasons)
                    .ThenInclude(s => s.Episodes)
                    .ThenInclude(e => e.WatchProgress)
                    .OrderByDescending(t => t.DateAdded)
                    .Take(12)
                    .ToListAsync(token);

                foreach (var show in recentShows)
                {
                    var episodes = show.Seasons.SelectMany(s => s.Episodes).Where(e => e.FileAvailable).ToList();
                    int totalEps = episodes.Count;
                    int unwatchedEps = episodes.Count(e => e.WatchProgress == null || !e.WatchProgress.IsCompleted);

                    recentShowsList.Add(new HomeMediaCard
                    {
                        Id = show.Id,
                        IsMovie = false,
                        IsEpisode = false,
                        Title = show.Title,
                        Subtitle = show.FirstAirYear > 0 ? show.FirstAirYear.ToString() : "Series",
                        PosterPath = show.LocalPosterPath ?? string.Empty,
                        UnwatchedCount = unwatchedEps,
                        IsWatched = totalEps > 0 && unwatchedEps == 0
                    });
                }
            }

            // 3. Load Recently Added Movies
            if (ShowRecentlyAddedMovies)
            {
                var recentMovies = await dbContext.MediaItems
                    .AsNoTracking()
                    .Where(m => m.FileAvailable)
                    .Include(m => m.WatchProgress)
                    .OrderByDescending(m => m.DateAdded)
                    .Take(12)
                    .ToListAsync(token);

                foreach (var movie in recentMovies)
                {
                    double progressPct = (movie.WatchProgress != null && movie.WatchProgress.DurationSeconds > 0 && movie.WatchProgress.PositionSeconds > 0)
                        ? (movie.WatchProgress.PositionSeconds * 100.0) / movie.WatchProgress.DurationSeconds
                        : 0;

                    recentMoviesList.Add(new HomeMediaCard
                    {
                        Id = movie.Id,
                        IsMovie = true,
                        IsEpisode = false,
                        Title = movie.Title,
                        Subtitle = movie.ReleaseYear > 0 ? movie.ReleaseYear.ToString() : "Movie",
                        PosterPath = movie.LocalPosterPath ?? string.Empty,
                        IsWatched = movie.WatchProgress?.IsCompleted ?? false,
                        ProgressPercent = progressPct
                    });
                }
            }

            if (token.IsCancellationRequested) return;

            void ApplyToUi()
            {
                ContinueWatchingItems.Clear();
                foreach (var item in continueWatchingList) ContinueWatchingItems.Add(item);

                RecentlyAddedShows.Clear();
                foreach (var item in recentShowsList) RecentlyAddedShows.Add(item);

                RecentlyAddedMovies.Clear();
                foreach (var item in recentMoviesList) RecentlyAddedMovies.Add(item);

                IsLibraryEmpty = libraryEmpty;
                IsAllRowsEmptyOrHidden = !libraryEmpty
                    && ContinueWatchingItems.Count == 0
                    && RecentlyAddedShows.Count == 0
                    && RecentlyAddedMovies.Count == 0;
            }

            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(ApplyToUi);
            }
            else
            {
                ApplyToUi();
            }
        }
        catch (OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load HomeView data.");
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    [RelayCommand]
    private void CardClicked(HomeMediaCard card)
    {
        if (card == null) return;

        if (card.IsFromContinueWatching || card.IsEpisode)
        {
            _navigationService.NavigateTo<Views.PlayerView>(new Services.PlayerNavigationArgs(card.Id, IsMovie: card.IsMovie));
        }
        else if (card.IsMovie)
        {
            if (card.ProgressPercent > 0 && !card.IsWatched)
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
            // Recently added TV show -> go to DetailView
            _navigationService.NavigateTo<Views.DetailView>(card.Id);
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
                    var episodes = fullShow.Seasons.SelectMany(s => s.Episodes).Where(e => e.FileAvailable).ToList();
                    bool anyUnwatched = episodes.Any(e => e.WatchProgress == null || !e.WatchProgress.IsCompleted);
                    bool newWatchedStatus = anyUnwatched;

                    await _playbackTracker.SetEpisodesWatchedAsync(episodes.Select(e => e.Id), newWatchedStatus);

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
