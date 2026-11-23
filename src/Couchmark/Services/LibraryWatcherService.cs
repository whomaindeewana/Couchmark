using Couchmark.Constants;
using Couchmark.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Couchmark.Services;

/// <summary>
/// Background service that monitors configured library folders for changes
/// (new files, deleted files, renamed files) and triggers the ILibraryScanner
/// to update the database in real-time.
/// </summary>
public class LibraryWatcherService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LibraryWatcherService> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly object _watchersLock = new object();

    public LibraryWatcherService(
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<LibraryWatcherService> logger)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StartWatching();

        // Listen for configuration changes (e.g. when appsettings.local.json is saved)
        // to reload the watchers.
        Microsoft.Extensions.Primitives.ChangeToken.OnChange(
            () => _configuration.GetReloadToken(),
            StartWatching);

        return Task.CompletedTask;
    }

    private void StartWatching()
    {
        lock (_watchersLock)
        {
            // Cleanup existing watchers
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();

            string[] moviePaths = _configuration.GetSection("Libraries:MoviePaths").Get<string[]>() ?? [];
            string[] tvPaths = _configuration.GetSection("Libraries:TvShowPaths").Get<string[]>() ?? [];

            foreach (string path in moviePaths.Concat(tvPaths))
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogWarning("Watcher skipping missing directory: {Path}", path);
                    continue;
                }

                var watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    IncludeSubdirectories = true
                };

                watcher.Created += (s, e) => SafeInvokeAsync(() => OnFileCreatedAsync(e.FullPath), e.FullPath);
                watcher.Deleted += (s, e) => SafeInvokeAsync(() => OnFileDeletedAsync(e.FullPath), e.FullPath);
                watcher.Renamed += (s, e) => SafeInvokeAsync(async () =>
                {
                    await OnFileDeletedAsync(e.OldFullPath);
                    await OnFileCreatedAsync(e.FullPath);
                }, e.FullPath);

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                _logger.LogInformation("Started watching directory: {Path}", path);
            }
        }
    }
    
    private void SafeInvokeAsync(Func<Task> action, string path)
    {
        Task.Run(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watcher handler failed for {Path}", path);
            }
        }).FireAndForgetSafeAsync();
    }

    private async Task OnFileCreatedAsync(string fullPath)
    {
        using var scope = _serviceProvider.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<ILibraryScanner>();

        if (Directory.Exists(fullPath))
        {
            _logger.LogInformation("Watcher detected new directory: {Path}", fullPath);
            try
            {
                var files = Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
                    .Where(IsVideoFile);

                foreach (var file in files)
                {
                    await scanner.AddFileAsync(file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning new directory: {Path}", fullPath);
            }
            return;
        }

        if (!IsVideoFile(fullPath)) return;

        _logger.LogInformation("Watcher detected new file: {Path}", fullPath);
        await scanner.AddFileAsync(fullPath);
    }

    private async Task OnFileDeletedAsync(string fullPath)
    {
        using var scope = _serviceProvider.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<ILibraryScanner>();

        if (IsVideoFile(fullPath))
        {
            _logger.LogInformation("Watcher detected deleted file: {Path}", fullPath);
            await scanner.RemoveFileAsync(fullPath);
        }
        else
        {
            // Could be a deleted directory
            _logger.LogInformation("Watcher detected deleted directory or non-video path: {Path}", fullPath);
            await scanner.RemoveDirectoryAsync(fullPath);
        }
    }

    private static bool IsVideoFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return AppConstants.VideoExtensions.Contains(ext);
    }

    public override void Dispose()
    {
        lock (_watchersLock)
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }
            _watchers.Clear();
        }
        
        GC.SuppressFinalize(this);
        base.Dispose();
    }
}
