namespace Couchmark.Services;

/// <summary>
/// Concrete implementation of INavigationService.
/// In Phase 1 this is a stub — navigation is handled directly by
/// WPF-UI's NavigationView in ShellWindow. This service is reserved
/// for ViewModel-initiated navigation in later phases.
/// </summary>
public class CouchmarkNavigationService : ICouchmarkNavigationService
{
    private Wpf.Ui.Controls.NavigationView? _navigationView;
    private object? _parameter;

    public void SetNavigationView(Wpf.Ui.Controls.NavigationView navigationView)
    {
        _navigationView = navigationView;
        
        // Hook into the Navigated event to pass parameters to the ViewModel safely
        _navigationView.Navigated += (s, e) => 
        {
            if (e.Page is System.Windows.FrameworkElement fe && _parameter is not null)
            {
                if (fe.DataContext is ViewModels.DetailViewModel dvm)
                {
                    if (_parameter is PlayerNavigationArgs args)
                    {
                        if (args.IsMovie)
                            _ = dvm.InitializeMovieAsync(args.Id);
                        else
                            _ = dvm.InitializeAsync(args.Id);
                    }
                    else if (_parameter is long showId)
                    {
                        _ = dvm.InitializeAsync(showId);
                    }
                }
                else if (fe.DataContext is ViewModels.PlayerViewModel pvm)
                {
                    if (_parameter is PlayerNavigationArgs args)
                    {
                        _ = pvm.InitializeAsync(args);
                    }
                    else if (_parameter is long rawId)
                    {
                        _ = pvm.InitializeMediaAsync(rawId);
                    }
                }
                _parameter = null;
            }
        };
    }

    public bool CanGoBack => _navigationView?.CanGoBack ?? false;

    public void NavigateTo<TPage>() where TPage : class
    {
        _navigationView?.Navigate(typeof(TPage));
    }

    public void NavigateTo<TPage>(object parameter) where TPage : class
    {
        _parameter = parameter;
        _navigationView?.Navigate(typeof(TPage));
    }

    public void GoBack()
    {
        _navigationView?.GoBack();
    }
}
