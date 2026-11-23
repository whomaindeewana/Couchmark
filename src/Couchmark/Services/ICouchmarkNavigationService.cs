namespace Couchmark.Services;

/// <summary>
/// Handles in-app page navigation for the WPF-UI NavigationView.
/// Abstracts away the WPF-UI navigation API so ViewModels can request
/// navigation without depending directly on UI types.
/// </summary>
public interface ICouchmarkNavigationService
{
    /// <summary>
    /// Sets the NavigationView instance to be used for navigation.
    /// Called once by ShellWindow on startup.
    /// </summary>
    void SetNavigationView(Wpf.Ui.Controls.NavigationView navigationView);

    /// <summary>
    /// Navigates to the specified page type.
    /// The page must be registered in the DI container.
    /// </summary>
    /// <typeparam name="TPage">The page (View) type to navigate to.</typeparam>
    void NavigateTo<TPage>() where TPage : class;

    /// <summary>
    /// Navigates to a page and passes a data object to its ViewModel.
    /// The ViewModel must implement a method or property to receive the parameter.
    /// </summary>
    /// <typeparam name="TPage">The page (View) type to navigate to.</typeparam>
    /// <param name="parameter">Data to pass to the page's ViewModel.</param>
    void NavigateTo<TPage>(object parameter) where TPage : class;

    /// <summary>
    /// Navigates back to the previous page in the navigation stack, if available.
    /// </summary>
    void GoBack();

    /// <summary>True if there is a page in the back stack to navigate to.</summary>
    bool CanGoBack { get; }
}
