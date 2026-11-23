using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace Couchmark.Services;

/// <summary>
/// Implements WPF-UI's <see cref="Wpf.Ui.IPageService"/> to bridge
/// NavigationView page resolution with the DI container.
///
/// Without this, NavigationView tries to create pages via
/// Activator.CreateInstance() — which fails because our pages have
/// constructor-injected dependencies (ViewModels, etc.).
///
/// This service is registered as a Singleton in App.xaml.cs and passed
/// to NavigationView via SetPageService() in ShellWindow.
/// </summary>
public class PageService : Wpf.Ui.IPageService
{
    private readonly IServiceProvider _serviceProvider;

    public PageService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Resolves a page instance from the DI container by its Type.
    /// Called internally by NavigationView when navigating.
    /// </summary>
    public FrameworkElement? GetPage(Type pageType)
    {
        return _serviceProvider.GetService(pageType) as FrameworkElement;
    }

    /// <summary>
    /// Generic overload — resolves a page by its generic type parameter.
    /// </summary>
    public T? GetPage<T>() where T : class
    {
        return _serviceProvider.GetService(typeof(T)) as T;
    }
}
