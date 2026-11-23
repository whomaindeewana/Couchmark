using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Couchmark.Data.Entities;
using Couchmark.Extensions;
using Couchmark.Helpers;
using Couchmark.Services;
using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.IO;

namespace Couchmark.ViewModels;

public partial class PlayerViewModel : ObservableObject, IDisposable, Wpf.Ui.Controls.INavigationAware
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICouchmarkNavigationService _navigationService;
    private readonly IPlaybackTracker _playbackTracker;
    private readonly Wpf.Ui.ISnackbarService _snackbarService;
    
    private long? _currentEpisodeId;
    private long? _currentMediaItemId;
    private Media? _currentMedia;
    private int _savedVolume = 100;
    private System.Windows.Threading.DispatcherTimer? _autoPlayTimer;
    private long? _pendingNextEpisodeId;

    // Skip Intro / Skip Outro timestamp window (seconds)
    private long _introStartSeconds;
    private long _introEndSeconds;
    private long _outroStartSeconds;
    private long _outroEndSeconds;

    public PlayerViewModel(
        IServiceScopeFactory scopeFactory,
        ICouchmarkNavigationService navigationService,
        IPlaybackTracker playbackTracker,
        Wpf.Ui.ISnackbarService snackbarService)
    {
        _scopeFactory = scopeFactory;
        _navigationService = navigationService;
        _playbackTracker = playbackTracker;
        _snackbarService = snackbarService;

        LibVLC = new LibVLC();
        MediaPlayer = new MediaPlayer(LibVLC);

        MediaPlayer.Playing += (s, e) =>
        {
            IsPlaying = true;
            // Allow LibVLC to parse audio/subtitle streams once playing
            Task.Delay(800).ContinueWith(_ => RefreshTracks());
        };
        MediaPlayer.Paused += (s, e) => IsPlaying = false;
        MediaPlayer.Stopped += (s, e) => IsPlaying = false;
        
        long lastSaveTime = 0;

        MediaPlayer.TimeChanged += (s, e) =>
        {
            long pos = e.Time / 1000;
            if (!IsUserSeeking)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    PositionSeconds = pos;
                    UpdateTimeDisplay();

                    // Show/hide Skip Intro button when within the intro window
                    if (_introEndSeconds > 0)
                    {
                        IsSkipIntroVisible = pos >= _introStartSeconds && pos < _introEndSeconds;
                    }

                    // Show/hide Skip Outro button when within the outro window
                    if (_outroEndSeconds > 0)
                    {
                        IsSkipOutroVisible = pos >= _outroStartSeconds && pos < _outroEndSeconds;
                    }
                });
            }
            
            // Throttle saves to every 5 seconds
            if (Math.Abs(pos - lastSaveTime) >= 5)
            {
                lastSaveTime = pos;
                _playbackTracker.UpdatePositionAsync(pos, DurationSeconds).FireAndForgetSafeAsync();
            }
        };

        MediaPlayer.LengthChanged += (s, e) =>
        {
            long dur = e.Length / 1000;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                DurationSeconds = dur;
                UpdateTimeDisplay();
            });
        };
        
        MediaPlayer.EndReached += (s, e) =>
        {
            HandleEndedAsync().FireAndForgetSafeAsync();
        };
    }

    public LibVLC LibVLC { get; }
    public MediaPlayer MediaPlayer { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private long _positionSeconds;

    [ObservableProperty]
    private long _durationSeconds;

    [ObservableProperty]
    private string _timeDisplayText = "00:00 / 00:00";

    [ObservableProperty]
    private bool _isUserSeeking;

    [ObservableProperty]
    private int _volume = 100;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _controlsVisible = true;

    [ObservableProperty]
    private bool _isEpisode = true;

    // ── Track Switching ───────────────────────────────────────────────────────
    public ObservableCollection<MediaTrackInfo> AudioTracks { get; } = [];
    public ObservableCollection<MediaTrackInfo> SubtitleTracks { get; } = [];

    [ObservableProperty]
    private MediaTrackInfo? _selectedAudioTrack;

    [ObservableProperty]
    private MediaTrackInfo? _selectedSubtitleTrack;

    partial void OnSelectedAudioTrackChanged(MediaTrackInfo? value)
    {
        if (value != null && MediaPlayer != null)
        {
            MediaPlayer.SetAudioTrack(value.Id);
        }
    }

    partial void OnSelectedSubtitleTrackChanged(MediaTrackInfo? value)
    {
        if (value != null && MediaPlayer != null)
        {
            MediaPlayer.SetSpu(value.Id);
        }
    }

    // ── Auto-Play Countdown ───────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isAutoPlayCountdownActive;

    [ObservableProperty]
    private int _autoPlayCountdownSeconds = 10;

    [ObservableProperty]
    private string _nextEpisodeTitle = string.Empty;

    // ── Skip Intro / Skip Outro ────────────────────────────────────────────────

    /// <summary>True while the player is within the intro window — shows the "Skip Intro" button.</summary>
    [ObservableProperty]
    private bool _isSkipIntroVisible;

    /// <summary>True while the player is within the outro window — shows the "Skip Outro" button.</summary>
    [ObservableProperty]
    private bool _isSkipOutroVisible;

    /// <summary>Seeks past the intro (to IntroEndSeconds) when the user taps Skip Intro.</summary>
    [RelayCommand]
    private void SkipIntro() => SeekTo(_introEndSeconds);

    /// <summary>Seeks past the outro (to OutroEndSeconds) when the user taps Skip Outro.</summary>
    [RelayCommand]
    private void SkipOutro() => SeekTo(_outroEndSeconds);

    // ── Live Skip Timestamps Markers ──────────────────────────────────────────

    [ObservableProperty]
    private bool _isTimestampsDialogOpen;

    [ObservableProperty]
    private string _editIntroStartText = string.Empty;

    [ObservableProperty]
    private string _editIntroEndText = string.Empty;

    [ObservableProperty]
    private string _editOutroStartText = string.Empty;

    [ObservableProperty]
    private string _editOutroEndText = string.Empty;

    [ObservableProperty]
    private bool _applyToEntireSeason;

    [ObservableProperty]
    private bool _canApplyToSeason;

    [RelayCommand]
    private void OpenTimestampsDialog()
    {
        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Pause();
        }

        EditIntroStartText = TimestampParser.FormatSeconds((int)_introStartSeconds);
        EditIntroEndText = TimestampParser.FormatSeconds((int)_introEndSeconds);
        EditOutroStartText = TimestampParser.FormatSeconds((int)_outroStartSeconds);
        EditOutroEndText = TimestampParser.FormatSeconds((int)_outroEndSeconds);
        CanApplyToSeason = _currentEpisodeId.HasValue;
        ApplyToEntireSeason = false;
        IsTimestampsDialogOpen = true;
        ControlsVisible = true;
    }

    [RelayCommand]
    private void CloseTimestampsDialog()
    {
        IsTimestampsDialogOpen = false;
    }

    [RelayCommand]
    private void GrabCurrentForIntroStart()
    {
        EditIntroStartText = TimestampParser.FormatSeconds((int)PositionSeconds);
    }

    [RelayCommand]
    private void GrabCurrentForIntroEnd()
    {
        EditIntroEndText = TimestampParser.FormatSeconds((int)PositionSeconds);
    }

    [RelayCommand]
    private void GrabCurrentForOutroStart()
    {
        EditOutroStartText = TimestampParser.FormatSeconds((int)PositionSeconds);
    }

    [RelayCommand]
    private void GrabCurrentForOutroEnd()
    {
        EditOutroEndText = TimestampParser.FormatSeconds((int)PositionSeconds);
    }

    [RelayCommand]
    private async Task SaveTimestampsFromPlayerAsync()
    {
        int introStart = TimestampParser.ParseToSeconds(EditIntroStartText);
        int introEnd = TimestampParser.ParseToSeconds(EditIntroEndText);
        int outroStart = TimestampParser.ParseToSeconds(EditOutroStartText);
        int outroEnd = TimestampParser.ParseToSeconds(EditOutroEndText);

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

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            if (_currentEpisodeId.HasValue)
            {
                if (ApplyToEntireSeason)
                {
                    var currentEp = await db.Episodes.FirstOrDefaultAsync(e => e.Id == _currentEpisodeId.Value);
                    if (currentEp != null)
                    {
                        var seasonEpisodes = await db.Episodes.Where(e => e.SeasonId == currentEp.SeasonId).ToListAsync();
                        foreach (var ep in seasonEpisodes)
                        {
                            var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.EpisodeId == ep.Id);
                            if (record == null)
                            {
                                record = new IntroTimestamp { EpisodeId = ep.Id };
                                db.IntroTimestamps.Add(record);
                            }
                            record.IntroStartSeconds = introStart;
                            record.IntroEndSeconds = introEnd;
                            record.OutroStartSeconds = outroStart;
                            record.OutroEndSeconds = outroEnd;
                        }
                        await db.SaveChangesAsync();
                        _snackbarService.Show("Timestamps Saved", "Applied skip timestamps to all episodes in this season.",
                            Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));
                    }
                }
                else
                {
                    var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.EpisodeId == _currentEpisodeId.Value);
                    if (record == null)
                    {
                        record = new IntroTimestamp { EpisodeId = _currentEpisodeId.Value };
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
            else if (_currentMediaItemId.HasValue)
            {
                var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.MediaItemId == _currentMediaItemId.Value);
                if (record == null)
                {
                    record = new IntroTimestamp { MediaItemId = _currentMediaItemId.Value };
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

            _introStartSeconds = introStart;
            _introEndSeconds = introEnd;
            _outroStartSeconds = outroStart;
            _outroEndSeconds = outroEnd;

            if (_introEndSeconds > 0)
                IsSkipIntroVisible = PositionSeconds >= _introStartSeconds && PositionSeconds < _introEndSeconds;
            if (_outroEndSeconds > 0)
                IsSkipOutroVisible = PositionSeconds >= _outroStartSeconds && PositionSeconds < _outroEndSeconds;

            IsTimestampsDialogOpen = false;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save timestamps from player");
            _snackbarService.Show("Error", "Could not save timestamps.",
                Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
        }
    }

    [RelayCommand]
    private async Task ClearTimestampsFromPlayerAsync()
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            if (_currentEpisodeId.HasValue)
            {
                var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.EpisodeId == _currentEpisodeId.Value);
                if (record != null)
                {
                    db.IntroTimestamps.Remove(record);
                    await db.SaveChangesAsync();
                }
            }
            else if (_currentMediaItemId.HasValue)
            {
                var record = await db.IntroTimestamps.FirstOrDefaultAsync(t => t.MediaItemId == _currentMediaItemId.Value);
                if (record != null)
                {
                    db.IntroTimestamps.Remove(record);
                    await db.SaveChangesAsync();
                }
            }

            _introStartSeconds = 0;
            _introEndSeconds = 0;
            _outroStartSeconds = 0;
            _outroEndSeconds = 0;
            IsSkipIntroVisible = false;
            IsSkipOutroVisible = false;
            EditIntroStartText = string.Empty;
            EditIntroEndText = string.Empty;
            EditOutroStartText = string.Empty;
            EditOutroEndText = string.Empty;

            _snackbarService.Show("Timestamps Cleared", "Skip timestamps cleared.",
                Wpf.Ui.Controls.ControlAppearance.Success, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Checkmark24), TimeSpan.FromSeconds(3));

            IsTimestampsDialogOpen = false;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to clear timestamps from player");
        }
    }

    partial void OnVolumeChanged(int value)
    {
        if (MediaPlayer != null)
        {
            MediaPlayer.Volume = value;
        }
        IsMuted = value == 0;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            IsMuted = false;
            Volume = _savedVolume > 0 ? _savedVolume : 50;
        }
        else
        {
            _savedVolume = Volume;
            IsMuted = true;
            Volume = 0;
        }
    }

    /// <summary>
    /// Unified navigation initializer handling both movies and episodes.
    /// </summary>
    public async Task InitializeAsync(PlayerNavigationArgs args)
    {
        if (args.IsMovie)
        {
            await InitializeMovieAsync(args.Id);
        }
        else
        {
            await InitializeEpisodeAsync(args.Id);
        }
    }

    /// <summary>
    /// Fallback initializer when only a raw media ID is provided.
    /// Checks database for an episode first, then a movie.
    /// </summary>
    public async Task InitializeMediaAsync(long id)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

        var ep = await db.Episodes.FirstOrDefaultAsync(e => e.Id == id);
        if (ep != null)
        {
            await InitializeEpisodeAsync(id);
            return;
        }

        var movie = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == id);
        if (movie != null)
        {
            await InitializeMovieAsync(id);
            return;
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
        {
            _snackbarService.Show("Media Not Found", "The requested media could not be found in the database.",
                Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
            GoBack();
        });
    }

    public async Task InitializeEpisodeAsync(long episodeId)
    {
        StopAutoPlayTimer();
        IsAutoPlayCountdownActive = false;
        _currentEpisodeId = episodeId;
        _currentMediaItemId = null;
        IsEpisode = true;
        
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

        var ep = await db.Episodes.FirstOrDefaultAsync(e => e.Id == episodeId);
        if (ep == null || !File.Exists(ep.FilePath))
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
            {
                _snackbarService.Show("File Not Found", "The video file for this episode could not be found.",
                    Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
                GoBack();
            });
            return;
        }

        Title = $"S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2} - {ep.Title}";

        var saved = await _playbackTracker.GetProgressAsync(null, episodeId);
        long resumeMs = (saved?.PositionSeconds ?? 0) * 1000;

        // Start tracking
        await _playbackTracker.StartTrackingAsync(null, episodeId, saved?.PositionSeconds ?? 0);

        // Load intro/outro timestamps for this episode (if any have been set)
        await LoadIntroTimestampAsync(db, null, episodeId);

        _currentMedia?.Dispose();
        _currentMedia = new Media(LibVLC, new Uri(ep.FilePath));
        MediaPlayer.Play(_currentMedia);
        
        if (resumeMs > 0) 
        {
            EventHandler<EventArgs>? onPlaying = null;
            onPlaying = (s, e) => 
            {
                MediaPlayer.Time = resumeMs;
                MediaPlayer.Playing -= onPlaying;
            };
            MediaPlayer.Playing += onPlaying;
        }
    }
    
    public async Task InitializeMovieAsync(long mediaItemId)
    {
        StopAutoPlayTimer();
        IsAutoPlayCountdownActive = false;
        _currentMediaItemId = mediaItemId;
        _currentEpisodeId = null;
        IsEpisode = false;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

        var movie = await db.MediaItems.FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (movie == null || !File.Exists(movie.FilePath))
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
            {
                _snackbarService.Show("File Not Found", "The video file for this movie could not be found.",
                    Wpf.Ui.Controls.ControlAppearance.Danger, new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(3));
                GoBack();
            });
            return;
        }

        Title = movie.Title;
        var saved = await _playbackTracker.GetProgressAsync(mediaItemId, null);
        long resumeMs = (saved?.PositionSeconds ?? 0) * 1000;

        await _playbackTracker.StartTrackingAsync(mediaItemId, null, saved?.PositionSeconds ?? 0);

        // Load intro/outro timestamps for this movie (if any have been set)
        await LoadIntroTimestampAsync(db, mediaItemId, null);

        _currentMedia?.Dispose();
        _currentMedia = new Media(LibVLC, new Uri(movie.FilePath));
        MediaPlayer.Play(_currentMedia);
        
        if (resumeMs > 0) 
        {
            EventHandler<EventArgs>? onPlaying = null;
            onPlaying = (s, e) => 
            {
                MediaPlayer.Time = resumeMs;
                MediaPlayer.Playing -= onPlaying;
            };
            MediaPlayer.Playing += onPlaying;
        }
    }

    public void SeekRelative(long seconds)
    {
        if (MediaPlayer != null && DurationSeconds > 0)
        {
            long newTime = Math.Clamp(MediaPlayer.Time + (seconds * 1000), 0, DurationSeconds * 1000);
            MediaPlayer.Time = newTime;
            PositionSeconds = newTime / 1000;
            UpdateTimeDisplay();
        }
    }

    public void SeekTo(long seconds)
    {
        if (MediaPlayer != null && DurationSeconds > 0)
        {
            long clamped = Math.Clamp(seconds, 0, DurationSeconds);
            MediaPlayer.Time = clamped * 1000;
            PositionSeconds = clamped;
            UpdateTimeDisplay();

            if (_introEndSeconds > 0 && clamped >= _introEndSeconds)
            {
                IsSkipIntroVisible = false;
            }
            if (_outroEndSeconds > 0 && clamped >= _outroEndSeconds)
            {
                IsSkipOutroVisible = false;
            }
        }
    }

    /// <summary>
    /// Loads IntroTimestamp record for the currently playing episode or movie from SQLite.
    /// Resets timestamp windows to 0 if none is configured.
    /// </summary>
    private async Task LoadIntroTimestampAsync(Couchmark.Data.CouchmarkDbContext db, long? mediaItemId, long? episodeId)
    {
        _introStartSeconds = 0;
        _introEndSeconds = 0;
        _outroStartSeconds = 0;
        _outroEndSeconds = 0;
        IsSkipIntroVisible = false;
        IsSkipOutroVisible = false;

        try
        {
            IntroTimestamp? intro = null;
            if (episodeId.HasValue)
            {
                intro = await db.IntroTimestamps
                    .AsNoTracking()
                    .FirstOrDefaultAsync(it => it.EpisodeId == episodeId.Value);

                if (intro == null)
                {
                    // Fallback to any configured intro in the same season
                    var currentEp = await db.Episodes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == episodeId.Value);
                    if (currentEp != null)
                    {
                        intro = await db.IntroTimestamps
                            .AsNoTracking()
                            .Where(t => t.Episode != null && t.Episode.SeasonId == currentEp.SeasonId)
                            .FirstOrDefaultAsync();
                    }
                }
            }
            else if (mediaItemId.HasValue)
            {
                intro = await db.IntroTimestamps
                    .AsNoTracking()
                    .FirstOrDefaultAsync(it => it.MediaItemId == mediaItemId.Value);
            }

            if (intro != null)
            {
                _introStartSeconds = intro.IntroStartSeconds;
                _introEndSeconds = intro.IntroEndSeconds;
                _outroStartSeconds = intro.OutroStartSeconds;
                _outroEndSeconds = intro.OutroEndSeconds;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load intro timestamp for MediaItemId={MediaItemId}, EpisodeId={EpisodeId}", mediaItemId, episodeId);
        }
    }

    private void UpdateTimeDisplay()
    {
        TimeDisplayText = $"{FormatTime(PositionSeconds)} / {FormatTime(DurationSeconds)}";
    }

    private static string FormatTime(long seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 
            ? ts.ToString(@"hh\:mm\:ss") 
            : ts.ToString(@"mm\:ss");
    }

    public void RefreshTracks()
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AudioTracks.Clear();
                var audioDescs = MediaPlayer.AudioTrackDescription;
                if (audioDescs != null)
                {
                    foreach (var desc in audioDescs)
                    {
                        var info = new MediaTrackInfo(desc.Id, string.IsNullOrWhiteSpace(desc.Name) ? $"Audio Track {desc.Id}" : desc.Name);
                        AudioTracks.Add(info);
                        if (desc.Id == MediaPlayer.AudioTrack)
                        {
                            SelectedAudioTrack = info;
                        }
                    }
                }

                SubtitleTracks.Clear();
                var subDescs = MediaPlayer.SpuDescription;
                if (subDescs != null)
                {
                    foreach (var desc in subDescs)
                    {
                        string name = string.IsNullOrWhiteSpace(desc.Name) 
                            ? (desc.Id == -1 ? "Subtitles Off" : $"Subtitle {desc.Id}") 
                            : desc.Name;
                        var info = new MediaTrackInfo(desc.Id, name);
                        SubtitleTracks.Add(info);
                        if (desc.Id == MediaPlayer.Spu)
                        {
                            SelectedSubtitleTrack = info;
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to refresh LibVLC tracks.");
        }
    }

    [RelayCommand]
    public void SelectAudioTrack(int trackId)
    {
        if (MediaPlayer != null)
        {
            MediaPlayer.SetAudioTrack(trackId);
            var found = AudioTracks.FirstOrDefault(t => t.Id == trackId);
            if (found != null) SelectedAudioTrack = found;
        }
    }

    [RelayCommand]
    public void SelectSubtitleTrack(int trackId)
    {
        if (MediaPlayer != null)
        {
            MediaPlayer.SetSpu(trackId);
            var found = SubtitleTracks.FirstOrDefault(t => t.Id == trackId);
            if (found != null) SelectedSubtitleTrack = found;
        }
    }

    [RelayCommand]
    private async Task PlayNextEpisodeAsync()
    {
        await HandleEndedAsync();
    }

    private async Task HandleEndedAsync()
    {
        if (_currentEpisodeId.HasValue)
        {
            await _playbackTracker.SetWatchedAsync(null, _currentEpisodeId, true);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<Couchmark.Data.CouchmarkDbContext>();

            var currentEp = await db.Episodes
                .Include(e => e.Season)
                .FirstOrDefaultAsync(e => e.Id == _currentEpisodeId.Value);

            if (currentEp != null)
            {
                var nextEp = await db.Episodes
                    .Where(e => e.SeasonId == currentEp.SeasonId && e.EpisodeNumber > currentEp.EpisodeNumber && e.FileAvailable)
                    .OrderBy(e => e.EpisodeNumber)
                    .FirstOrDefaultAsync();

                if (nextEp == null && currentEp.Season != null)
                {
                    var currentSeason = currentEp.Season;
                    var nextSeason = await db.Seasons
                        .Include(s => s.Episodes)
                        .Where(s => s.TvShowId == currentSeason.TvShowId && s.SeasonNumber > currentSeason.SeasonNumber)
                        .OrderBy(s => s.SeasonNumber)
                        .FirstOrDefaultAsync();

                    if (nextSeason != null)
                    {
                        nextEp = nextSeason.Episodes
                            .Where(e => e.FileAvailable)
                            .OrderBy(e => e.EpisodeNumber)
                            .FirstOrDefault();
                    }
                }

                if (nextEp != null && File.Exists(nextEp.FilePath))
                {
                    _pendingNextEpisodeId = nextEp.Id;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        NextEpisodeTitle = $"S{nextEp.SeasonNumber:D2}E{nextEp.EpisodeNumber:D2} - {nextEp.Title}";
                        AutoPlayCountdownSeconds = 10;
                        IsAutoPlayCountdownActive = true;

                        _autoPlayTimer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(1)
                        };
                        _autoPlayTimer.Tick += (s, e) =>
                        {
                            AutoPlayCountdownSeconds--;
                            if (AutoPlayCountdownSeconds <= 0)
                            {
                                PlayNowAutoPlay();
                            }
                        };
                        _autoPlayTimer.Start();
                    });
                    return;
                }
            }
        }
        else if (_currentMediaItemId.HasValue)
        {
            await _playbackTracker.SetWatchedAsync(_currentMediaItemId, null, true);
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => GoBack());
    }

    [RelayCommand]
    private void PlayNowAutoPlay()
    {
        StopAutoPlayTimer();
        IsAutoPlayCountdownActive = false;
        if (_pendingNextEpisodeId.HasValue)
        {
            long epId = _pendingNextEpisodeId.Value;
            _pendingNextEpisodeId = null;
            InitializeEpisodeAsync(epId).FireAndForgetSafeAsync();
        }
    }

    [RelayCommand]
    private void CancelAutoPlay()
    {
        StopAutoPlayTimer();
        IsAutoPlayCountdownActive = false;
        _pendingNextEpisodeId = null;
        GoBack();
    }

    private void StopAutoPlayTimer()
    {
        if (_autoPlayTimer != null)
        {
            _autoPlayTimer.Stop();
            _autoPlayTimer = null;
        }
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (MediaPlayer.IsPlaying)
            MediaPlayer.Pause();
        else
            MediaPlayer.Play();
    }

    [RelayCommand]
    private void Stop()
    {
        GoBack();
    }

    [RelayCommand]
    private void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    private void GoBack()
    {
        IsFullscreen = false;
        StopAutoPlayTimer();
        _playbackTracker.StopTrackingAsync().FireAndForgetSafeAsync();
        
        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Stop();
        }
        
        _navigationService.GoBack();
    }
    
    public void OnNavigatedTo() { }

    public void OnNavigatedFrom() 
    { 
        Dispose();
    }

    public void Dispose()
    {
        IsFullscreen = false;
        StopAutoPlayTimer();
        _playbackTracker.StopTrackingAsync().FireAndForgetSafeAsync();
        if (MediaPlayer.IsPlaying) MediaPlayer.Stop();
        _currentMedia?.Dispose();
        MediaPlayer.Dispose();
        LibVLC.Dispose();
    }
}
