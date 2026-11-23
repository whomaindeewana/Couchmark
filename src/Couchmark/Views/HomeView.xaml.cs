using Couchmark.ViewModels;
using System.Windows.Controls;

namespace Couchmark.Views;

/// <summary>
/// Code-behind for HomeView.xaml.
/// The ViewModel is injected via the DI container and assigned as DataContext.
/// </summary>
public partial class HomeView : Page
{
    public HomeView(HomeViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
