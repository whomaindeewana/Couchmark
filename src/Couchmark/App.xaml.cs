using Couchmark.Data;
using Couchmark.Services;
using Couchmark.ViewModels;
using Couchmark.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using System.Windows;
using Wpf.Ui;

namespace Couchmark;

/// <summary>
/// Application entry point and Dependency Injection (DI) host.
/// </summary>
public partial class App : Application
{
    private IHost? _host = null;

    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Error(e.Exception, "Unhandled UI dispatcher exception.");
            MessageBox.Show($"A fatal error occurred: {e.Exception.Message}", "Couchmark Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Current.Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Error(ex, "Unhandled AppDomain exception.");
            }
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Move Core.Initialize() here (L-1)
            LibVLCSharp.Shared.Core.Initialize();

            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Constants.AppConstants.AppName);

            Directory.CreateDirectory(Path.Combine(appDataPath, Constants.AppConstants.PosterCacheFolder));
            Directory.CreateDirectory(Path.Combine(appDataPath, Constants.AppConstants.LogsFolder));

            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile(Path.Combine(appDataPath, "appsettings.local.json"), optional: true, reloadOnChange: true)
                .Build();

            string logPath = Path.Combine(
                appDataPath,
                Constants.AppConstants.LogsFolder,
                "couchmark-.log");

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Couchmark starting up.");

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    string dbPath = Path.Combine(appDataPath, Constants.AppConstants.DatabaseFileName);
                    services.AddDbContext<CouchmarkDbContext>(options =>
                    {
                        options.UseSqlite($"Data Source={dbPath}");
                        options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
                    });

                    services.AddHttpClient();

                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddSingleton<ICouchmarkNavigationService, CouchmarkNavigationService>();
                    services.AddSingleton<IPlaybackTracker, PlaybackTracker>();
                    services.AddSingleton<ILibraryScanner, LibraryScanner>();
                    services.AddSingleton<IMetadataService, MetadataService>();
                    
                    services.AddHostedService<LibraryWatcherService>();

                    services.AddSingleton<ISnackbarService, SnackbarService>();
                    services.AddSingleton<IContentDialogService, ContentDialogService>();
                    services.AddSingleton<Wpf.Ui.IPageService, PageService>();

                    services.AddTransient<ShellViewModel>();
                    services.AddTransient<HomeViewModel>();
                    services.AddTransient<LibraryViewModel>();
                    services.AddTransient<MoviesViewModel>();
                    services.AddTransient<DetailViewModel>();
                    services.AddTransient<PlayerViewModel>();
                    services.AddTransient<SettingsViewModel>();

                    services.AddSingleton<ShellWindow>();
                    services.AddTransient<HomeView>();
                    services.AddTransient<LibraryView>();
                    services.AddTransient<MoviesView>();
                    services.AddTransient<DetailView>();
                    services.AddTransient<PlayerView>();
                    services.AddTransient<SettingsView>();
                })
                .Build();

            await _host.StartAsync();

            using (IServiceScope scope = _host.Services.CreateScope())
            {
                CouchmarkDbContext db = scope.ServiceProvider.GetRequiredService<CouchmarkDbContext>();
                await db.Database.MigrateAsync();
                Log.Information("Database migrations applied. DB path: {DbPath}",
                    db.Database.GetDbConnection().DataSource);
            }

            string startupTheme = configuration["Appearance:Theme"] ?? "Midnight";
            Themes.ThemeHelper.ApplyTheme(startupTheme);

            ShellWindow shell = _host.Services.GetRequiredService<ShellWindow>();
            shell.Show();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start application.");
            MessageBox.Show($"Application failed to start: {ex.Message}", "Couchmark Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try 
            {
                IPlaybackTracker tracker = _host.Services.GetRequiredService<IPlaybackTracker>();
                await tracker.StopTrackingAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop tracking on exit.");
            }

            Log.Information("Couchmark shutting down.");
            Log.CloseAndFlush();

            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}

