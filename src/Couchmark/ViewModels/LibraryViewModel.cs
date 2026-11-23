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

public partial class LibraryViewModel : ObservableObject, Wpf.Ui.Controls.INavigationAware
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICouchmarkNavigationService _navigationService;
    private readonly IPlaybackTracker _playbackTracker;
    private readonly Wpf.Ui.ISnackbarService _snackbarService;
    private List<TvShow> _allShows = [];

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

    public ObservableCollection<TvShow> TvShows { get; } = [];

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
    private void OpenFileLocation(TvShow? show)
    {
        if (show == null) return;
        var firstEp = show.Seasons.FirstOrDefault()?.Episodes.FirstOrDefault();
        string? path = firstEp?.FilePath;
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
    private async Task ToggleWatchedAsync(TvShow? show)
    {
        if (show == null) return;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.CouchmarkDbContext>();

        var fullShow = await db.TvShows
            .Include(t => t.Seasons)
            .ThenInclude(s => s.Episodes)
            .ThenInclude(e => e.WatchProgress)
            .FirstOrDefaultAsync(t => t.Id == show.Id);

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

            _snackbarService.Show(
                newWatchedStatus ? "Marked All Watched" : "Marked All Unwatched",
                $"{(newWatchedStatus ? "Marked all" : "Unmarked all")} episodes of {show.Title}.",
                Wpf.Ui.Controls.ControlAppearance.Success,
                new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(2));
        }
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
            
            _allShows = await db.TvShows
                .Include(t => t.Seasons)
                .ThenInclude(s => s.Episodes)
                .ToListAsync();

            ApplyFilterAndSort();
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load TV shows for library view.");
        }
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<TvShow> filtered = _allShows;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim().ToLowerInvariant();
            filtered = filtered.Where(s => s.Title.ToLowerInvariant().Contains(q) || (s.Genres != null && s.Genres.ToLowerInvariant().Contains(q)));
        }

        filtered = SelectedSortOption switch
        {
            "Title Z-A" => filtered.OrderByDescending(s => s.Title),
            "Rating (High to Low)" => filtered.OrderByDescending(s => s.TmdbRating),
            "Recently Added" => filtered.OrderByDescending(s => s.DateAdded),
            _ => filtered.OrderBy(s => s.Title)
        };

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            TvShows.Clear();
            foreach (var show in filtered)
            {
                TvShows.Add(show);
            }
        });
    }
}
