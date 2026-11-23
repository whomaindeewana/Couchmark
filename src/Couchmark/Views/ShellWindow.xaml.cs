using Couchmark.Services;
using Couchmark.ViewModels;
using System.Windows;
using Wpf.Ui;

namespace Couchmark.Views;

/// <summary>
/// Code-behind for ShellWindow.xaml — the main application window.
///
/// Responsibilities:
/// - Receives the ShellViewModel and services via constructor injection (DI)
/// - Wires up IPageService so NavigationView resolves pages from DI
/// - Registers the snackbar presenter so ISnackbarService can show toast notifications
/// - Navigates to the Home page on first load
/// </summary>
public partial class ShellWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private readonly ISnackbarService _snackbarService;
    private readonly IPageService _pageService;
    private readonly ICouchmarkNavigationService _navigationService;

    /// <summary>
    /// Constructor — all parameters are injected by the DI container (see App.xaml.cs).
    /// </summary>
    public ShellWindow(
        ShellViewModel viewModel,
        ISnackbarService snackbarService,
        IPageService pageService,
        ICouchmarkNavigationService navigationService)
    {
        _viewModel = viewModel;
        _snackbarService = snackbarService;
        _pageService = pageService;
        _navigationService = navigationService;

        DataContext = _viewModel;
        InitializeComponent();
    }

    /// <summary>
    /// Called when the NavigationView finishes loading.
    /// Sets up the page service, registers the snackbar presenter,
    /// and navigates to the Home page.
    /// </summary>
    private void RootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        // Register snackbar presenter so ISnackbarService.Show() works from anywhere
        _snackbarService.SetSnackbarPresenter(SnackbarPresenter);

        // Wire up the page service — this tells NavigationView to resolve page
        // instances from the DI container instead of using Activator.CreateInstance()
        RootNavigation.SetPageService(_pageService);
        
        // Give the navigation service a reference to the NavigationView so ViewModels can trigger navigation
        _navigationService.SetNavigationView(RootNavigation);

        // Navigate to Home on startup
        RootNavigation.Navigate(typeof(HomeView));
    }
}
