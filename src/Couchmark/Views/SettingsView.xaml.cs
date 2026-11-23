using Couchmark.ViewModels;
using System.Windows.Controls;

namespace Couchmark.Views;

/// <summary>
/// Code-behind for SettingsView.xaml.
/// The ViewModel handles all settings logic via commands and bindings.
/// </summary>
public partial class SettingsView : Page
{
    public SettingsView(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
