using Couchmark.ViewModels;
using System.Windows.Controls;

namespace Couchmark.Views;

/// <summary>Code-behind for LibraryView.xaml.</summary>
public partial class LibraryView : Page
{
    public LibraryView(LibraryViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
