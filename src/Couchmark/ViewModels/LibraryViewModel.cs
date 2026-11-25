using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Couchmark.Data.Entities;
using Couchmark.Extensions;
using Couchmark.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Couchmark.ViewModels;

public partial class TvShowCardItem : ObservableObject
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Genres { get; set; } = string.Empty;
    public int FirstAirYear { get; set; }
    public double TmdbRating { get; set; }
    public DateTime DateAdded { get; set; }
    public string LocalPosterPath { get; set; } = string.Empty;
    public string? FirstEpisodeFilePath { get; set; }
    public List<long> EpisodeIds { get; set; } = [];

    [ObservableProperty]
    private int _totalEpisodes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnwatched))]
    private int _unwatchedCount;

    [ObservableProperty]
    private bool _isWatched;

    public bool HasUnwatched => UnwatchedCount > 0;
}

public partial class LibraryViewModel : ObservableObject, Wpf.Ui.Controls.INavigationAware
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICouchmarkNavigationService _navigationService;
    private readonly IPlaybackTracker _playbackTracker;
    private readonly Wpf.Ui.ISnackbarService _snackbarService;
    private List<TvShowCardItem> _allShows = [];
    private CancellationTokenSource? _loadCts;
    private bool _hasLoaded;

    public LibraryViewModel(
        IServiceScopeFactory scopeFactory,
        ICouchmarkNavigationService navigationService,
        IPlaybackTracker playbackTracker,
        Wpf.Ui.ISnackbarService snackbarService)
    {
        _scopeFactory = scopeFactory;
        _navigationService = navigationService;
        _playbackTracker = playbackTracker;
        _snackbarService = snackbarService;
    }

    public ObservableCollection<TvShowCardItem> TvShows { get; } = [];

    [ObservableProperty]
    private bool _isLibraryEmpty;

    [ObservableProperty]
    private bool _isSearchEmpty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value) => ApplyFilterAndSort();

    [ObservableProperty]
    private string _selectedSortOption = "Title A-Z";

    partial void OnSelectedSortOptionChanged(string value) => ApplyFilterAndSort();

    public ObservableCollection<string> AvailableSortOptions { get; } = new(new[]
    {
        "Title A-Z",
        "Title Z-A",
        "Rating (High to Low)",
        "Recently Added"
    });

    [RelayCommand]
    private void NavigateToDetail(long showId)
    {
        _navigationService.NavigateTo<Views.DetailView>(showId);
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.NavigateTo<Views.SettingsView>();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private void OpenFileLocation(TvShowCardItem? show)
    {
        if (show == null) return;
        string? path = show.FirstEpisodeFilePath;
        if (!string.IsNullOrEmpty(path))
        {
            string? folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{folder}\"",
                        UseShellExecute = true
                    });
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to open folder location for show");
                }
            }
        }
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync(TvShowCardItem? show)
    {
        if (show == null) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Data.CouchmarkDbContext>();

            var fullShow = await db.TvShows
                .AsNoTracking()
                .Include(t => t.Seasons)
                .ThenInclude(s => s.Episodes)
                .ThenInclude(e => e.WatchProgress)
                .FirstOrDefaultAsync(t => t.Id == show.Id);

            if (fullShow != null)
            {
                var episodes = fullShow.Seasons.SelectMany(s => s.Episodes).Where(e => e.FileAvailable).ToList();
                bool anyUnwatched = episodes.Any(e => e.WatchProgress == null || !e.WatchProgress.IsCompleted);
                bool newWatchedStatus = anyUnwatched;

                await _playbackTracker.SetEpisodesWatchedAsync(episodes.Select(e => e.Id), newWatchedStatus);

                show.TotalEpisodes = episodes.Count;
                show.UnwatchedCount = newWatchedStatus ? 0 : episodes.Count;
                show.IsWatched = episodes.Count > 0 && newWatchedStatus;

                _snackbarService.Show(
                    newWatchedStatus ? "Marked All Watched" : "Marked All Unwatched",
                    $"{(newWatchedStatus ? "Marked all" : "Unmarked all")} episodes of {show.Title}.",
                    Wpf.Ui.Controls.ControlAppearance.Success,
                    new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                    TimeSpan.FromSeconds(2));
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to toggle watched status for TV show {ShowTitle}", show.Title);
        }
    }

    public void OnNavigatedTo()
    {
        LoadDataAsync().FireAndForgetSafeAsync();
    }

    public void OnNavigatedFrom()
    {
        _loadCts?.Cancel();
    }

    private async Task LoadDataAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Data.CouchmarkDbContext>();

            var shows = await db.TvShows
                .AsNoTracking()
                .Where(t => t.Seasons.Any(s => s.Episodes.Any(e => e.FileAvailable)))
                .Include(t => t.Seasons)
                .ThenInclude(s => s.Episodes)
                .ThenInclude(e => e.WatchProgress)
                .ToListAsync(token);

            if (token.IsCancellationRequested) return;

            _allShows = shows.Select(s =>
            {
                var episodes = s.Seasons.SelectMany(se => se.Episodes).Where(e => e.FileAvailable).ToList();
                int total = episodes.Count;
                int unwatched = episodes.Count(e => e.WatchProgress == null || !e.WatchProgress.IsCompleted);
                return new TvShowCardItem
                {
                    Id = s.Id,
                    Title = s.Title,
                    Genres = s.Genres ?? string.Empty,
                    FirstAirYear = s.FirstAirYear,
                    TmdbRating = s.TmdbRating,
                    DateAdded = s.DateAdded,
                    LocalPosterPath = s.LocalPosterPath ?? string.Empty,
                    FirstEpisodeFilePath = episodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.FilePath))?.FilePath,
                    EpisodeIds = episodes.Select(e => e.Id).ToList(),
                    TotalEpisodes = total,
                    UnwatchedCount = unwatched,
                    IsWatched = total > 0 && unwatched == 0
                };
            }).ToList();

            _hasLoaded = true;
            ApplyFilterAndSort();
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load TV shows for library view.");
        }
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<TvShowCardItem> filtered = _allShows;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim().ToLowerInvariant();
            filtered = filtered.Where(s => s.Title.ToLowerInvariant().Contains(q) || s.Genres.ToLowerInvariant().Contains(q));
        }

        filtered = SelectedSortOption switch
        {
            "Title Z-A" => filtered.OrderByDescending(s => s.Title),
            "Rating (High to Low)" => filtered.OrderByDescending(s => s.TmdbRating),
            "Recently Added" => filtered.OrderByDescending(s => s.DateAdded),
            _ => filtered.OrderBy(s => s.Title)
        };

        var filteredList = filtered.ToList();

        void UpdateUi()
        {
            TvShows.Clear();
            foreach (var show in filteredList)
            {
                TvShows.Add(show);
            }

            IsLibraryEmpty = _hasLoaded && _allShows.Count == 0;
            IsSearchEmpty = _hasLoaded && _allShows.Count > 0 && filteredList.Count == 0;
        }

        if (System.Windows.Application.Current?.Dispatcher != null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(UpdateUi);
        }
        else
        {
            UpdateUi();
        }
    }
}
