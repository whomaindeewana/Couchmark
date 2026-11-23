using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Couchmark.Themes;

public static class ThemeHelper
{
    public static void ApplyTheme(string themeName)
    {
        string theme = themeName?.ToLowerInvariant() ?? "midnight";

        switch (theme)
        {
            case "white":
                // Clean crisp modern light mode
                ApplicationThemeManager.Apply(ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.None);
                SetPalette(
                    bg: "#FFFFFF",
                    card: "#F3F4F6",
                    cardBorder: "#E5E7EB",
                    textPrimary: "#111827",
                    textSecondary: "#4B5563"
                );
                break;

            case "nude":
                // Soothing warm sand / linen cream light mode (gentle on eyes)
                ApplicationThemeManager.Apply(ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.None);
                SetPalette(
                    bg: "#F5ECE1",
                    card: "#E8DAC8",
                    cardBorder: "#D5C4AF",
                    textPrimary: "#2C2219",
                    textSecondary: "#665545"
                );
                break;

            case "midnight":
                // Cool deep indigo navy dark mode
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None);
                SetPalette(
                    bg: "#0B132B",
                    card: "#1C2541",
                    cardBorder: "#3A506B",
                    textPrimary: "#EDF2F4",
                    textSecondary: "#8D99AE"
                );
                break;

            case "vanta black":
                // Pure OLED pitch-black dark mode
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None);
                SetPalette(
                    bg: "#000000",
                    card: "#121212",
                    cardBorder: "#262626",
                    textPrimary: "#FFFFFF",
                    textSecondary: "#888888"
                );
                break;

            default:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None);
                SetPalette(
                    bg: "#0B132B",
                    card: "#1C2541",
                    cardBorder: "#3A506B",
                    textPrimary: "#EDF2F4",
                    textSecondary: "#8D99AE"
                );
                break;
        }

        // Apply background directly to open windows
        if (Application.Current != null)
        {
            var bgBrush = (Brush)Application.Current.Resources["CouchmarkBackgroundBrush"];
            foreach (Window win in Application.Current.Windows)
            {
                win.Background = bgBrush;
            }
        }
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
