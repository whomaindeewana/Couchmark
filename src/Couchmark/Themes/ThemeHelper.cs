using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Couchmark.Themes;

public record ThemePalette(
    ApplicationTheme BaseTheme,
    string Background,
    string Card,
    string CardBorder,
    string TextPrimary,
    string TextSecondary);

public static class ThemeHelper
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private static string _currentThemeName = "Midnight";
    private static bool _isWatchingSystemTheme;
    private static readonly object _watchLock = new();

    public static bool IsSystemLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            object? value = key?.GetValue(AppsUseLightThemeValue);
            if (value is int intValue)
            {
                return intValue != 0;
            }
        }
        catch
        {
            // Fall back to dark palette if registry access is unavailable
        }

        return false;
    }

    public static ThemePalette ResolvePalette(string? themeName, bool isSystemLight)
    {
        string theme = themeName?.Trim().ToLowerInvariant() ?? "midnight";

        if (theme == "system")
        {
            theme = isSystemLight ? "white" : "midnight";
        }

        return theme switch
        {
            "white" => new ThemePalette(
                ApplicationTheme.Light,
                Background: "#FFFFFF",
                Card: "#F3F4F6",
                CardBorder: "#E5E7EB",
                TextPrimary: "#111827",
                TextSecondary: "#4B5563"),

            "nude" => new ThemePalette(
                ApplicationTheme.Light,
                Background: "#F5ECE1",
                Card: "#E8DAC8",
                CardBorder: "#D5C4AF",
                TextPrimary: "#2C2219",
                TextSecondary: "#665545"),

            "vanta black" => new ThemePalette(
                ApplicationTheme.Dark,
                Background: "#000000",
                Card: "#121212",
                CardBorder: "#262626",
                TextPrimary: "#FFFFFF",
                TextSecondary: "#888888"),

            _ => new ThemePalette(
                ApplicationTheme.Dark,
                Background: "#0B132B",
                Card: "#1C2541",
                CardBorder: "#3A506B",
                TextPrimary: "#EDF2F4",
                TextSecondary: "#8D99AE")
        };
    }

    public static void ApplyTheme(string? themeName)
    {
        _currentThemeName = string.IsNullOrWhiteSpace(themeName) ? "Midnight" : themeName.Trim();

        if (_currentThemeName.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            EnsureWatchingSystemTheme();
        }
        else
        {
            StopWatchingSystemTheme();
        }

        ThemePalette palette = ResolvePalette(_currentThemeName, IsSystemLightTheme());

        if (Application.Current != null)
        {
            ApplicationThemeManager.Apply(palette.BaseTheme, Wpf.Ui.Controls.WindowBackdropType.None);
            SetPalette(palette.Background, palette.Card, palette.CardBorder, palette.TextPrimary, palette.TextSecondary);

            // Apply background directly to open windows
            var bgBrush = (Brush)Application.Current.Resources["CouchmarkBackgroundBrush"];
            foreach (Window win in Application.Current.Windows)
            {
                win.Background = bgBrush;
            }
        }
    }

    public static void StopWatchingSystemTheme()
    {
        lock (_watchLock)
        {
            if (_isWatchingSystemTheme)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _isWatchingSystemTheme = false;
            }
        }
    }

    private static void EnsureWatchingSystemTheme()
    {
        lock (_watchLock)
        {
            if (!_isWatchingSystemTheme)
            {
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
                _isWatchingSystemTheme = true;
            }
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (!_currentThemeName.Equals("system", StringComparison.OrdinalIgnoreCase)) return;

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_currentThemeName.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTheme("System");
            }
        });
    }

    private static void SetPalette(string bg, string card, string cardBorder, string textPrimary, string textSecondary)
    {
        SetBrush("CouchmarkBackgroundBrush", bg);
        SetBrush("CouchmarkCardBrush", card);
        SetBrush("CouchmarkCardBorderBrush", cardBorder);
        SetBrush("CouchmarkTextPrimaryBrush", textPrimary);
        SetBrush("CouchmarkTextSecondaryBrush", textSecondary);

        // Also update standard WPF-UI tokens so internal controls match seamlessly
        SetBrush("ApplicationBackgroundBrush", bg);
        SetBrush("CardBackgroundFillColorDefaultBrush", card);
        SetBrush("ControlFillColorDefaultBrush", card);
        SetBrush("CardStrokeColorDefaultBrush", cardBorder);
        SetBrush("TextFillColorPrimaryBrush", textPrimary);
        SetBrush("TextFillColorSecondaryBrush", textSecondary);
    }

    private static void SetBrush(string key, string hexColor)
    {
        if (Application.Current == null) return;
        var color = (Color)ColorConverter.ConvertFromString(hexColor);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }
}
