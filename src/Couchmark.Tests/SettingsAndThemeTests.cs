using Couchmark.Services;
using Couchmark.Themes;
using Couchmark.ViewModels;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Wpf.Ui.Appearance;
using Xunit;

namespace Couchmark.Tests;

public class SettingsAndThemeTests
{
    private sealed class DummyScanner : ILibraryScanner
    {
        public Task ScanAllAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddFileAsync(string filePath) => Task.CompletedTask;
        public Task RemoveFileAsync(string filePath) => Task.CompletedTask;
        public Task RemoveDirectoryAsync(string directoryPath) => Task.CompletedTask;
    }

    [Fact]
    public void ResolvePalette_System_FollowsLightAndDarkOsSetting()
    {
        ThemePalette light = ThemeHelper.ResolvePalette("System", isSystemLight: true);
        Assert.Equal(ApplicationTheme.Light, light.BaseTheme);
        Assert.Equal("#FFFFFF", light.Background);

        ThemePalette dark = ThemeHelper.ResolvePalette("system", isSystemLight: false);
        Assert.Equal(ApplicationTheme.Dark, dark.BaseTheme);
        Assert.Equal("#0B132B", dark.Background);
    }

    [Theory]
    [InlineData("White", ApplicationTheme.Light, "#FFFFFF")]
    [InlineData("Nude", ApplicationTheme.Light, "#F5ECE1")]
    [InlineData("Midnight", ApplicationTheme.Dark, "#0B132B")]
    [InlineData("Vanta Black", ApplicationTheme.Dark, "#000000")]
    [InlineData("UnknownTheme", ApplicationTheme.Dark, "#0B132B")]
    public void ResolvePalette_ReturnsExpectedPalette(string name, ApplicationTheme expectedBase, string expectedBg)
    {
        ThemePalette palette = ThemeHelper.ResolvePalette(name, isSystemLight: true);
        Assert.Equal(expectedBase, palette.BaseTheme);
        Assert.Equal(expectedBg, palette.Background);
    }

    [Fact]
    public void SettingsViewModel_IncludesSystemTheme_AndExportsSettingsToJson()
    {
        var configData = new Dictionary<string, string?>
        {
            ["ApiKeys:Tmdb"] = "tmdb-key",
            ["ApiKeys:Omdb"] = "omdb-key",
            ["Libraries:MoviePaths:0"] = @"D:\Movies",
            ["Libraries:TvShowPaths:0"] = @"D:\Shows",
            ["Appearance:Theme"] = "system",
            ["HomeRows:ShowContinueWatching"] = "true",
            ["HomeRows:ShowRecentlyAddedShows"] = "false",
            ["HomeRows:ShowRecentlyAddedMovies"] = "true",
            ["HomeRows:RowOrder"] = "Recently Added First"
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var vm = new SettingsViewModel(configuration, new DummyScanner());

        Assert.Contains("System", vm.AvailableThemes);
        Assert.Equal("System", vm.SelectedTheme);

        string tempFile = Path.Combine(Path.GetTempPath(), $"couchmark_export_{Guid.NewGuid()}.json");
        try
        {
            vm.ExportToFile(tempFile);
            Assert.True(File.Exists(tempFile));

            string json = File.ReadAllText(tempFile);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            Assert.Equal("tmdb-key", root.GetProperty("ApiKeys").GetProperty("Tmdb").GetString());
            Assert.Equal("omdb-key", root.GetProperty("ApiKeys").GetProperty("Omdb").GetString());
            Assert.Equal(@"D:\Movies", root.GetProperty("Libraries").GetProperty("MoviePaths")[0].GetString());
            Assert.Equal(@"D:\Shows", root.GetProperty("Libraries").GetProperty("TvShowPaths")[0].GetString());
            Assert.Equal("System", root.GetProperty("Appearance").GetProperty("Theme").GetString());
            Assert.False(root.GetProperty("HomeRows").GetProperty("ShowRecentlyAddedShows").GetBoolean());
            Assert.Equal("Recently Added First", root.GetProperty("HomeRows").GetProperty("RowOrder").GetString());
        }
        finally
        {
            ThemeHelper.StopWatchingSystemTheme();
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
