using Couchmark.ViewModels;
using System.Windows.Controls;

namespace Couchmark.Views;

public partial class MoviesView : Page
{
    public MoviesView(MoviesViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
