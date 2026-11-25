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

public partial class MovieCardItem : ObservableObject
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Genres { get; set; } = string.Empty;
    public int ReleaseYear { get; set; }
    public double TmdbRating { get; set; }
    public DateTime DateAdded { get; set; }
    public string LocalPosterPath { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private bool _isWatched;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private double _progressPercent;

    public bool ShowProgress => !IsWatched && ProgressPercent > 0 && ProgressPercent < 100;
}

public partial class MoviesViewModel : ObservableObject, Wpf.Ui.Controls.INavigationAware
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICouchmarkNavigationService _navigationService;
    private readonly IPlaybackTracker _playbackTracker;
    private readonly Wpf.Ui.ISnackbarService _snackbarService;
    private List<MovieCardItem> _allMovies = [];
    private CancellationTokenSource? _loadCts;
    private bool _hasLoaded;

    public MoviesViewModel(
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

    public ObservableCollection<MovieCardItem> Movies { get; } = [];

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
        "Recently Added",
        "Release Year"
    });

    [RelayCommand]
    private void NavigateToDetail(long movieId)
    {
        _navigationService.NavigateTo<Views.DetailView>(new PlayerNavigationArgs(movieId, IsMovie: true));
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
    private void PlayMovie(long movieId)
    {
        _navigationService.NavigateTo<Views.PlayerView>(new PlayerNavigationArgs(movieId, IsMovie: true));
    }

    [RelayCommand]
    private void OpenFileLocation(MovieCardItem? movie)
    {
        if (movie == null || string.IsNullOrEmpty(movie.FilePath) || !File.Exists(movie.FilePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{movie.FilePath}\"",
                UseShellExecute = true
            });
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to open file location for movie");
        }
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync(MovieCardItem? movie)
    {
        if (movie == null) return;

        try
        {
            var progress = await _playbackTracker.GetProgressAsync(movie.Id, null);
            bool currentWatched = progress?.IsCompleted ?? false;
            bool newWatched = !currentWatched;

            await _playbackTracker.SetWatchedAsync(movie.Id, null, newWatched);

            movie.IsWatched = newWatched;
            if (!newWatched)
            {
                movie.ProgressPercent = 0;
            }

            _snackbarService.Show(
                newWatched ? "Marked as Watched" : "Marked as Unwatched",
                $"{(newWatched ? "Marked" : "Unmarked")} {movie.Title} as watched.",
                Wpf.Ui.Controls.ControlAppearance.Success,
                new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(2));
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to toggle watched status for movie {MovieTitle}", movie.Title);
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

            var movies = await db.MediaItems
                .AsNoTracking()
                .Where(m => m.FileAvailable)
                .Include(m => m.WatchProgress)
                .ToListAsync(token);

            if (token.IsCancellationRequested) return;

            _allMovies = movies.Select(m => new MovieCardItem
            {
                Id = m.Id,
                Title = m.Title,
                Genres = m.Genres ?? string.Empty,
                ReleaseYear = m.ReleaseYear,
                TmdbRating = m.TmdbRating,
                DateAdded = m.DateAdded,
                LocalPosterPath = m.LocalPosterPath ?? string.Empty,
                FilePath = m.FilePath,
                IsWatched = m.WatchProgress?.IsCompleted ?? false,
                ProgressPercent = (m.WatchProgress != null && m.WatchProgress.DurationSeconds > 0 && m.WatchProgress.PositionSeconds > 0)
                    ? (m.WatchProgress.PositionSeconds * 100.0) / m.WatchProgress.DurationSeconds
                    : 0
            }).ToList();

            _hasLoaded = true;
            ApplyFilterAndSort();
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load movies.");
        }
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<MovieCardItem> filtered = _allMovies;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim().ToLowerInvariant();
            filtered = filtered.Where(m => m.Title.ToLowerInvariant().Contains(q) || m.Genres.ToLowerInvariant().Contains(q));
        }

        filtered = SelectedSortOption switch
        {
            "Title Z-A" => filtered.OrderByDescending(m => m.Title),
            "Rating (High to Low)" => filtered.OrderByDescending(m => m.TmdbRating),
            "Recently Added" => filtered.OrderByDescending(m => m.DateAdded),
            "Release Year" => filtered.OrderByDescending(m => m.ReleaseYear),
            _ => filtered.OrderBy(m => m.Title)
        };

        var filteredList = filtered.ToList();

        void UpdateUi()
        {
            Movies.Clear();
            foreach (var movie in filteredList)
            {
                Movies.Add(movie);
            }

            IsLibraryEmpty = _hasLoaded && _allMovies.Count == 0;
            IsSearchEmpty = _hasLoaded && _allMovies.Count > 0 && filteredList.Count == 0;
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
