using CommunityToolkit.Mvvm.ComponentModel;

namespace Couchmark.ViewModels;

/// <summary>
/// ViewModel for the ShellWindow (main application window).
/// Manages top-level state such as the window title and navigation header.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    /// <summary>
    /// The title shown in the window title bar.
    /// Bound to the WPF-UI TitleBar control.
    /// </summary>
    [ObservableProperty]
    private string _title = "Couchmark";
}
