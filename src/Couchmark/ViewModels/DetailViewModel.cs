using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Couchmark.Extensions;
using Couchmark.Helpers;
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

public class SeasonDisplayItem
{
    public long? SeasonId { get; set; }
    public int SeasonNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsWatched { get; set; }
    public double Opacity => IsAvailable ? 1.0 : 0.45;
}

public partial class DetailViewModel : ObservableObject, Wpf.Ui.Controls.INavigationAware
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICouchmarkNavigationService _navigationService;
    private readonly IMetadataService _metadataService;
    private readonly Wpf.Ui.ISnackbarService _snackbarService;
    private readonly IPlaybackTracker _playbackTracker;
    private CancellationTokenSource? _loadCts;
    private long _currentEntityId;

    public DetailViewModel(
        IServiceScopeFactory scopeFactory,
        ICouchmarkNavigationService navigationService,
        IMetadataService metadataService,
        Wpf.Ui.ISnackbarService snackbarService,
        IPlaybackTracker playbackTracker)
    {
        _scopeFactory = scopeFactory;
        _navigationService = navigationService;
        _metadataService = metadataService;
        _snackbarService = snackbarService;
        _playbackTracker = playbackTracker;
    }

    [RelayCommand]
    private void PlayEpisode(long episodeId)
    {
        _navigationService.NavigateTo<Views.PlayerView>(new PlayerNavigationArgs(episodeId, IsMovie: false));
    }

    [RelayCommand]
    private void PlayMovie()
    {
        if (Movie != null)
        {
            _navigationService.NavigateTo<Views.PlayerView>(new PlayerNavigationArgs(Movie.Id, IsMovie: true));
        }
    }

    [RelayCommand]
    private async Task RestartMovieAsync()
    {
        if (Movie != null)
        {
            await _playbackTracker.ResetProgressAsync(Movie.Id, null);
            _navigationService.NavigateTo<Views.PlayerView>(new PlayerNavigationArgs(Movie.Id, IsMovie: true));
        }
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync()
    {
        if (Movie != null)
        {
            bool newStatus = !IsWatched;
            await _playbackTracker.SetWatchedAsync(Movie.Id, null, newStatus);
            IsWatched = newStatus;
            WatchedButtonText = newStatus ? "Mark as Unwatched" : "Mark as Watched";
            WatchedStatusText = newStatus ? "Watched" : "Unwatched";
            ResumeButtonText = IsWatched ? "Watch Again" : "Play Movie";
            CanResume = false;
            
            _snackbarService.Show(
                newStatus ? "Marked Watched" : "Marked Unwatched",
                $"{(newStatus ? "Marked" : "Unmarked")} {Movie.Title} as watched.",
                Wpf.Ui.Controls.ControlAppearance.Success,
                new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(2));
        }
    }

    [RelayCommand]
    private void OpenFileLocation()
    {
        string? targetPath = Movie?.FilePath ?? Show?.Seasons.FirstOrDefault()?.Episodes.FirstOrDefault()?.FilePath;
        if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{targetPath}\"",
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to open explorer location");
            }
        }
    }

    [RelayCommand]
    private void OpenEpisodeFileLocation(Couchmark.Data.Entities.Episode? episode)
    {
        if (episode == null || string.IsNullOrEmpty(episode.FilePath) || !File.Exists(episode.FilePath)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{episode.FilePath}\"",
                UseShellExecute = true
            });
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to open file location for episode");
        }
    }

    [RelayCommand]
    private async Task ToggleEpisodeWatchedAsync(Couchmark.Data.Entities.Episode? episode)
    {
        if (episode == null) return;
        try
        {
            var progress = await _playbackTracker.GetProgressAsync(null, episode.Id);
            bool currentWatched = progress?.IsCompleted ?? false;
            bool newWatched = !currentWatched;

            await _playbackTracker.SetWatchedAsync(null, episode.Id, newWatched);

            if (SelectedSeason?.SeasonId != null)
            {
                await LoadEpisodesForSeasonAsync(SelectedSeason.SeasonId.Value);
            }

            _snackbarService.Show(
                newWatched ? "Marked as Watched" : "Marked as Unwatched",
                $"{(newWatched ? "Marked" : "Unmarked")} S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2} as watched.",
                Wpf.Ui.Controls.ControlAppearance.Success,
                new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24),
                TimeSpan.FromSeconds(2));
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to toggle watched status for episode");
        }
    }

    [ObservableProperty]
    private Couchmark.Data.Entities.TvShow? _show;

    [ObservableProperty]
    private Couchmark.Data.Entities.MediaItem? _movie;

    [ObservableProperty]
    private bool _isMovie;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _overview = string.Empty;

    [ObservableProperty]
    private string _backdropPath = string.Empty;

    [ObservableProperty]
    private string _posterPath = string.Empty;

    [ObservableProperty]
    private string _ratingText = string.Empty;

    [ObservableProperty]
    private string _yearText = string.Empty;

    [ObservableProperty]
    private string _runtimeText = string.Empty;

    [ObservableProperty]
    private string _genresText = string.Empty;

    [ObservableProperty]
    private bool _isWatched;

    [ObservableProperty]
    private string _watchedButtonText = "Mark as Watched";

    [ObservableProperty]
    private string _watchedStatusText = "Unwatched";

    [ObservableProperty]
    private bool _canResume;

    [ObservableProperty]
    private string _resumeButtonText = "Play Movie";

    public ObservableCollection<SeasonDisplayItem> AvailableSeasons { get; } = [];

    [ObservableProperty]
    private SeasonDisplayItem? _selectedSeason;

    partial void OnSelectedSeasonChanged(SeasonDisplayItem? oldValue, SeasonDisplayItem? newValue)
    {
        if (newValue != null && newValue.SeasonId.HasValue)
        {
            LoadEpisodesForSeasonAsync(newValue.SeasonId.Value).FireAndForgetSafeAsync();
        }
        else
        {
            Episodes.Clear();
        }
    }

    public ObservableCollection<Couchmark.Data.Entities.Episode> Episodes { get; } = [];

    // ── Fix Match Overlay ──────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isFixMatchOpen;

    [ObservableProperty]
    private string _fixMatchInput = string.Empty;

    [ObservableProperty]
    private bool _isFixMatching;

    [RelayCommand]
    private void OpenFixMatch()
    {
        FixMatchInput = (IsMovie ? Movie?.TmdbId?.ToString() ?? Movie?.Title : Show?.TmdbId?.ToString() ?? Show?.Title) ?? Title ?? string.Empty;
        IsFixMatchOpen = true;
    }

    [RelayCommand]
    private void CloseFixMatch()
    {
        IsFixMatchOpen = false;
    }

    [RelayCommand]
    private async Task ApplyFixMatchAsync()
    {
        string input = FixMatchInput?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            _snackbarService.Show("Input Required", "Please enter a title, TMDB ID, or TMDB URL.",
                Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            return;
        }

        IsFixMatching = true;

        try
        {
            int? tmdbId = null;

            // 1. Direct numeric TMDB ID
            if (int.TryParse(input, out int parsedId))
            {
                tmdbId = parsedId;
            }
            // 2. TMDB URL (e.g. /tv/123 or /movie/456)
            else if (IsMovie && System.Text.RegularExpressions.Regex.Match(input, @"/movie/(\d+)").Success)
            {
                var match = System.Text.RegularExpressions.Regex.Match(input, @"/movie/(\d+)");
                if (int.TryParse(match.Groups[1].Value, out int urlId)) tmdbId = urlId;
            }
            else if (!IsMovie && System.Text.RegularExpressions.Regex.Match(input, @"/tv/(\d+)").Success)
            {
                var match = System.Text.RegularExpressions.Regex.Match(input, @"/tv/(\d+)");
                if (int.TryParse(match.Groups[1].Value, out int urlId)) tmdbId = urlId;
            }

            // 3. Search TMDB by title
            if (!tmdbId.HasValue)
            {
                if (IsMovie)
                {
                    tmdbId = await _metadataService.SearchMovieAsync(input);
                }
                else
                {
                    tmdbId = await _metadataService.SearchTvShowAsync(input);
                }
            }

            if (!tmdbId.HasValue)
            {
                _snackbarService.Show("No Match Found", $"Could not find any match on TMDB for '{input}'.",
                    Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(4));
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            if (IsMovie)
            {
                var movie = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == _currentEntityId);
                if (movie != null)
                {
                    movie.TmdbId = tmdbId.Value;
                    await _metadataService.EnrichMovieAsync(movie);
                    await db.SaveChangesAsync();

                    _snackbarService.Show("Match Updated", $"Successfully updated metadata for {movie.Title}!",
                        Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));

                    IsFixMatchOpen = false;
                    await InitializeMovieAsync(_currentEntityId);
                }
            }
            else
            {
                var show = await db.TvShows
                    .Include(t => t.Seasons)
                    .ThenInclude(s => s.Episodes)
                    .FirstOrDefaultAsync(t => t.Id == _currentEntityId);

                if (show != null)
                {
                    show.TmdbId = tmdbId.Value;
                    await _metadataService.EnrichTvShowAsync(show);

                    foreach (var season in show.Seasons)
                    {
                        foreach (var ep in season.Episodes)
                        {
                            await _metadataService.EnrichEpisodeAsync(ep, tmdbId.Value);
                            if (!string.IsNullOrEmpty(ep.FilePath))
                            {
                                ep.FileAvailable = File.Exists(ep.FilePath);
                            }
                        }
                    }

                    await db.SaveChangesAsync();

                    _snackbarService.Show("Match Updated", $"Successfully updated metadata for {show.Title}!",
                        Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));

                    IsFixMatchOpen = false;
                    await InitializeAsync(_currentEntityId);
                }
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to apply fix match");
            _snackbarService.Show("Error", "Could not fetch metadata.",
                Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
        }
        finally
        {
            IsFixMatching = false;
        }
    }

    // ── Intro & Outro Timestamps ──────────────────────────────────────────────

    [ObservableProperty]
    private bool _isTimestampsModalOpen;

    [ObservableProperty]
    private string _timestampTargetTitle = string.Empty;

    [ObservableProperty]
    private string _introStartText = string.Empty;

    [ObservableProperty]
    private string _introEndText = string.Empty;

    [ObservableProperty]
    private string _outroStartText = string.Empty;

    [ObservableProperty]
    private string _outroEndText = string.Empty;

    [ObservableProperty]
    private bool _applyToEntireSeason;

    [ObservableProperty]
    private bool _canApplyToSeason;

    [ObservableProperty]
    private bool _isSavingTimestamps;

    private long? _targetTimestampEpisodeId;
    private long? _targetTimestampMediaItemId;

    [RelayCommand]
    private async Task OpenEpisodeTimestampsAsync(Couchmark.Data.Entities.Episode? episode)
    {
        if (episode == null) return;

        _targetTimestampEpisodeId = episode.Id;
        _targetTimestampMediaItemId = null;
        TimestampTargetTitle = $"S{episode.SeasonNumber:D2}E{episode.EpisodeNumber:D2} - {episode.Title}";
        CanApplyToSeason = true;
        ApplyToEntireSeason = false;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            var intro = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.EpisodeId == episode.Id);
            if (intro != null)
            {
                IntroStartText = TimestampParser.FormatSeconds(intro.IntroStartSeconds);
                IntroEndText = TimestampParser.FormatSeconds(intro.IntroEndSeconds);
                OutroStartText = TimestampParser.FormatSeconds(intro.OutroStartSeconds);
                OutroEndText = TimestampParser.FormatSeconds(intro.OutroEndSeconds);
            }
            else
            {
                // Fallback: Check if any other episode in this season has timestamps configured to use as default
                var seasonIntro = await db.IntroTimestamps
                    .Where(t => t.Episode != null && t.Episode.SeasonId == episode.SeasonId)
                    .FirstOrDefaultAsync();

                if (seasonIntro != null)
                {
                    IntroStartText = TimestampParser.FormatSeconds(seasonIntro.IntroStartSeconds);
                    IntroEndText = TimestampParser.FormatSeconds(seasonIntro.IntroEndSeconds);
                    OutroStartText = TimestampParser.FormatSeconds(seasonIntro.OutroStartSeconds);
                    OutroEndText = TimestampParser.FormatSeconds(seasonIntro.OutroEndSeconds);
                }
                else
                {
                    IntroStartText = string.Empty;
                    IntroEndText = string.Empty;
                    OutroStartText = string.Empty;
                    OutroEndText = string.Empty;
                }
            }
            IsTimestampsModalOpen = true;
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load episode intro timestamps");
        }
    }

    [RelayCommand]
    private async Task OpenMovieTimestampsAsync()
    {
        if (Movie == null) return;

        _targetTimestampMediaItemId = Movie.Id;
        _targetTimestampEpisodeId = null;
        TimestampTargetTitle = Movie.Title;
        CanApplyToSeason = false;
        ApplyToEntireSeason = false;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            var intro = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.MediaItemId == Movie.Id);
            if (intro != null)
            {
                IntroStartText = TimestampParser.FormatSeconds(intro.IntroStartSeconds);
                IntroEndText = TimestampParser.FormatSeconds(intro.IntroEndSeconds);
                OutroStartText = TimestampParser.FormatSeconds(intro.OutroStartSeconds);
                OutroEndText = TimestampParser.FormatSeconds(intro.OutroEndSeconds);
            }
            else
            {
                IntroStartText = string.Empty;
                IntroEndText = string.Empty;
                OutroStartText = string.Empty;
                OutroEndText = string.Empty;
            }
            IsTimestampsModalOpen = true;
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load movie intro timestamps");
        }
    }

    [RelayCommand]
    private void CloseTimestampsModal()
    {
        IsTimestampsModalOpen = false;
    }

    [RelayCommand]
    private async Task SaveTimestampsAsync()
    {
        int introStart = TimestampParser.ParseToSeconds(IntroStartText);
        int introEnd = TimestampParser.ParseToSeconds(IntroEndText);
        int outroStart = TimestampParser.ParseToSeconds(OutroStartText);
        int outroEnd = TimestampParser.ParseToSeconds(OutroEndText);

        if (introEnd > 0 && introEnd <= introStart)
        {
            _snackbarService.Show("Invalid Timestamps", "Intro End must be greater than Intro Start.",
                Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            return;
        }

        if (outroEnd > 0 && outroEnd <= outroStart)
        {
            _snackbarService.Show("Invalid Timestamps", "Outro End must be greater than Outro Start.",
                Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            return;
        }

        IsSavingTimestamps = true;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            if (_targetTimestampEpisodeId.HasValue)
            {
                if (ApplyToEntireSeason && SelectedSeason?.SeasonId != null)
                {
                    var episodes = await db.Episodes
                        .Where(e => e.SeasonId == SelectedSeason.SeasonId.Value)
                        .ToListAsync();

                    foreach (var ep in episodes)
                    {
                        var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.EpisodeId == ep.Id);
                        if (record == null)
                        {
                            record = new Couchmark.Data.Entities.IntroTimestamp { EpisodeId = ep.Id };
                            db.IntroTimestamps.Add(record);
                        }
                        record.IntroStartSeconds = introStart;
                        record.IntroEndSeconds = introEnd;
                        record.OutroStartSeconds = outroStart;
                        record.OutroEndSeconds = outroEnd;
                    }
                    await db.SaveChangesAsync();
                    _snackbarService.Show("Timestamps Saved", $"Applied skip timestamps to all {episodes.Count} episodes in {SelectedSeason.Title}.",
                        Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
                }
                else
                {
                    var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.EpisodeId == _targetTimestampEpisodeId.Value);
                    if (record == null)
                    {
                        record = new Couchmark.Data.Entities.IntroTimestamp { EpisodeId = _targetTimestampEpisodeId.Value };
                        db.IntroTimestamps.Add(record);
                    }
                    record.IntroStartSeconds = introStart;
                    record.IntroEndSeconds = introEnd;
                    record.OutroStartSeconds = outroStart;
                    record.OutroEndSeconds = outroEnd;
                    await db.SaveChangesAsync();
                    _snackbarService.Show("Timestamps Saved", "Skip timestamps saved for this episode.",
                        Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
                }
            }
            else if (_targetTimestampMediaItemId.HasValue)
            {
                var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.MediaItemId == _targetTimestampMediaItemId.Value);
                if (record == null)
                {
                    record = new Couchmark.Data.Entities.IntroTimestamp { MediaItemId = _targetTimestampMediaItemId.Value };
                    db.IntroTimestamps.Add(record);
                }
                record.IntroStartSeconds = introStart;
                record.IntroEndSeconds = introEnd;
                record.OutroStartSeconds = outroStart;
                record.OutroEndSeconds = outroEnd;
                await db.SaveChangesAsync();
                _snackbarService.Show("Timestamps Saved", "Skip timestamps saved for this movie.",
                    Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
            }

            IsTimestampsModalOpen = false;
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save skip timestamps");
            _snackbarService.Show("Error", "Could not save timestamps.",
                Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
        }
        finally
        {
            IsSavingTimestamps = false;
        }
    }

    [RelayCommand]
    private async Task ClearTimestampsAsync()
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            if (_targetTimestampEpisodeId.HasValue)
            {
                if (ApplyToEntireSeason && SelectedSeason?.SeasonId != null)
                {
                    var seasonEpIds = await db.Episodes
                        .Where(e => e.SeasonId == SelectedSeason.SeasonId.Value)
                        .Select(e => e.Id)
                        .ToListAsync();

                    var records = await db.IntroTimestamps
                        .Where(t => t.EpisodeId != null && seasonEpIds.Contains(t.EpisodeId.Value))
                        .ToListAsync();

                    db.IntroTimestamps.RemoveRange(records);
                    await db.SaveChangesAsync();
                    _snackbarService.Show("Timestamps Cleared", $"Cleared skip timestamps for all episodes in {SelectedSeason.Title}.",
                        Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
                }
                else
                {
                    var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.EpisodeId == _targetTimestampEpisodeId.Value);
                    if (record != null)
                    {
                        db.IntroTimestamps.Remove(record);
                        await db.SaveChangesAsync();
                    }
                    _snackbarService.Show("Timestamps Cleared", "Skip timestamps cleared for this episode.",
                        Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
                }
            }
            else if (_targetTimestampMediaItemId.HasValue)
            {
                var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.MediaItemId == _targetTimestampMediaItemId.Value);
                if (record != null)
                {
                    db.IntroTimestamps.Remove(record);
                    await db.SaveChangesAsync();
                }
                _snackbarService.Show("Timestamps Cleared", "Skip timestamps cleared for this movie.",
                    Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
            }

            IntroStartText = string.Empty;
            IntroEndText = string.Empty;
            OutroStartText = string.Empty;
            OutroEndText = string.Empty;
            IsTimestampsModalOpen = false;
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to clear skip timestamps");
            _snackbarService.Show("Error", "Could not clear timestamps.",
                Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
        }
    }

    public void OnNavigatedTo()
    {
        if (_currentEntityId != 0)
        {
            if (IsMovie)
            {
                _ = InitializeMovieAsync(_currentEntityId);
            }
            else
            {
                _ = InitializeAsync(_currentEntityId);
            }
        }
    }

    public void OnNavigatedFrom() 
    {
        _loadCts?.Cancel();
    }

    public async Task InitializeMovieAsync(long movieId)
    {
        _currentEntityId = movieId;
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            var movie = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == movieId, token);
            if (movie == null || token.IsCancellationRequested) return;

            var progress = await db.WatchProgress.FirstOrDefaultAsync(w => w.MediaItemId == movieId, token);

            bool isComp = progress?.IsCompleted ?? false;
            long pos = progress?.PositionSeconds ?? 0;
            bool canRes = pos > 0 && !isComp;

            string btnText = "Play Movie";
            if (canRes)
            {
                var ts = TimeSpan.FromSeconds(pos);
                btnText = $"Resume ({ts:hh\\:mm\\:ss})";
            }
            else if (isComp)
            {
                btnText = "Watch Again";
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsMovie = true;
                Movie = movie;
                Show = null;
                Title = movie.Title;
                Overview = movie.Overview;
                BackdropPath = movie.BackdropPath ?? string.Empty;
                PosterPath = movie.LocalPosterPath ?? string.Empty;
                YearText = movie.ReleaseYear > 0 ? movie.ReleaseYear.ToString() : string.Empty;
                RuntimeText = movie.RuntimeMinutes > 0 ? $"{movie.RuntimeMinutes} mins" : string.Empty;
                GenresText = movie.Genres ?? string.Empty;
                RatingText = movie.TmdbRating > 0 ? $"{movie.TmdbRating:F1}" : (movie.ImdbRating ?? string.Empty);
                IsWatched = isComp;
                WatchedButtonText = isComp ? "Mark as Unwatched" : "Mark as Watched";
                WatchedStatusText = isComp ? "Watched" : "Unwatched";
                CanResume = canRes;
                ResumeButtonText = btnText;

                AvailableSeasons.Clear();
                Episodes.Clear();
            });
        }
        catch (System.Exception ex) when (ex is not System.OperationCanceledException)
        {
            Serilog.Log.Error(ex, "Failed to initialize movie detail view");
        }
    }

    public async Task InitializeAsync(long showId)
    {
        _currentEntityId = showId;
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            var show = await db.TvShows
                .Include(t => t.Seasons)
                .ThenInclude(s => s.Episodes)
                .ThenInclude(e => e.WatchProgress)
                .FirstOrDefaultAsync(t => t.Id == showId, token);

            if (show == null || token.IsCancellationRequested) return;

            int maxFoundSeason = show.Seasons.Count > 0 ? show.Seasons.Max(s => s.SeasonNumber) : 1;
            int totalSeasons = Math.Max(show.TotalSeasonsOnTmdb, maxFoundSeason);
            if (totalSeasons <= 0) totalSeasons = 1;

            var seasonItems = new System.Collections.Generic.List<SeasonDisplayItem>();

            for (int sNum = 1; sNum <= totalSeasons; sNum++)
            {
                var existingSeason = show.Seasons.FirstOrDefault(s => s.SeasonNumber == sNum);
                if (existingSeason != null)
                {
                    bool hasAvailableFiles = existingSeason.Episodes.Any(e => e.FileAvailable);
                    bool allWatched = existingSeason.Episodes.Count > 0 && 
                                       existingSeason.Episodes.All(e => e.WatchProgress != null && e.WatchProgress.IsCompleted);

                    seasonItems.Add(new SeasonDisplayItem
                    {
                        SeasonId = existingSeason.Id,
                        SeasonNumber = sNum,
                        Title = string.IsNullOrWhiteSpace(existingSeason.Title) ? $"Season {sNum}" : existingSeason.Title,
                        IsAvailable = hasAvailableFiles,
                        IsWatched = allWatched
                    });
                }
                else
                {
                    // Ghost Season (exists on TMDB or in total series, but not in local library)
                    seasonItems.Add(new SeasonDisplayItem
                    {
                        SeasonId = null,
                        SeasonNumber = sNum,
                        Title = $"Season {sNum}",
                        IsAvailable = false,
                        IsWatched = false
                    });
                }
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsMovie = false;
                Show = show;
                Movie = null;
                Title = show.Title;
                Overview = show.Overview;
                BackdropPath = show.BackdropPath ?? string.Empty;
                PosterPath = show.LocalPosterPath ?? string.Empty;
                YearText = show.FirstAirYear > 0 ? show.FirstAirYear.ToString() : string.Empty;
                RuntimeText = string.Empty;
                GenresText = show.Genres ?? string.Empty;
                RatingText = show.TmdbRating > 0 ? $"{show.TmdbRating:F1}" : (show.ImdbRating ?? string.Empty);

                AvailableSeasons.Clear();
                foreach (var item in seasonItems)
                {
                    AvailableSeasons.Add(item);
                }

                SelectedSeason = AvailableSeasons.FirstOrDefault(s => s.IsAvailable) ?? AvailableSeasons.FirstOrDefault();
            });
        }
        catch (System.Exception ex) when (ex is not System.OperationCanceledException)
        {
            Serilog.Log.Error(ex, "Failed to initialize detail view");
        }
    }

    private async Task LoadEpisodesForSeasonAsync(long seasonId)
    {
        var token = _loadCts?.Token ?? CancellationToken.None;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            var episodes = await db.Episodes
                .Include(e => e.WatchProgress)
                .Where(e => e.SeasonId == seasonId)
                .OrderBy(e => e.EpisodeNumber)
                .ToListAsync(token);

            if (token.IsCancellationRequested) return;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Episodes.Clear();
                foreach (var ep in episodes)
                {
                    Episodes.Add(ep);
                }
            });
        }
        catch (System.Exception ex) when (ex is not System.OperationCanceledException)
        {
            Serilog.Log.Error(ex, "Failed to load episodes for season");
        }
    }
}
