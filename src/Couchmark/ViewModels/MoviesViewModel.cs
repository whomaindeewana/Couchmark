using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Couchmark.Data.Entities;
using Couchmark.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Couchmark.ViewModels;

public partial class MoviesViewModel : ObservableObject, Wpf.Ui.Controls.INavigationAware
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICouchmarkNavigationService _navigationService;
    private readonly IPlaybackTracker _playbackTracker;
    private readonly Wpf.Ui.ISnackbarService _snackbarService;
    private List<MediaItem> _allMovies = [];

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

    public ObservableCollection<MediaItem> Movies { get; } = [];

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
    private void PlayMovie(long movieId)
    {
        _navigationService.NavigateTo<Views.PlayerView>(new PlayerNavigationArgs(movieId, IsMovie: true));
    }

    [RelayCommand]
    private void OpenFileLocation(MediaItem? movie)
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
    private async Task ToggleWatchedAsync(MediaItem? movie)
    {
        if (movie == null) return;
        var progress = await _playbackTracker.GetProgressAsync(movie.Id, null);
        bool currentWatched = progress?.IsCompleted ?? false;
        bool newWatched = !currentWatched;

        await _playbackTracker.SetWatchedAsync(movie.Id, null, newWatched);

        _snackbarService.Show(
            newWatched ? "Marked as Watched" : "Marked as Unwatched",
            $"{(newWatched ? "Marked" : "Unmarked")} {movie.Title} as watched.",
            Wpf.Ui.Controls.ControlAppearance.Success,
            new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
            TimeSpan.FromSeconds(2));
    }

    public void OnNavigatedTo()
    {
        _ = LoadDataAsync();
    }

    public void OnNavigatedFrom() { }

    private async Task LoadDataAsync()
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Data.CouchmarkDbContext>();

            _allMovies = await db.MediaItems.ToListAsync();
            ApplyFilterAndSort();
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load movies.");
        }
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<MediaItem> filtered = _allMovies;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim().ToLowerInvariant();
            filtered = filtered.Where(m => m.Title.ToLowerInvariant().Contains(q) || (m.Genres != null && m.Genres.ToLowerInvariant().Contains(q)));
        }

        filtered = SelectedSortOption switch
        {
            "Title Z-A" => filtered.OrderByDescending(m => m.Title),
            "Rating (High to Low)" => filtered.OrderByDescending(m => m.TmdbRating),
            "Recently Added" => filtered.OrderByDescending(m => m.DateAdded),
            "Release Year" => filtered.OrderByDescending(m => m.ReleaseYear),
            _ => filtered.OrderBy(m => m.Title)
        };

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Movies.Clear();
            foreach (var movie in filtered)
            {
                Movies.Add(movie);
            }
        });
    }
}
