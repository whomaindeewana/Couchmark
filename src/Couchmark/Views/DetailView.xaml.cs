using Couchmark.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Couchmark.Views;

/// <summary>Code-behind for DetailView.xaml.</summary>
public partial class DetailView : Page
{
    public DetailView(DetailViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        Focusable = true;
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (viewModel.IsTimestampsModalOpen)
                {
                    viewModel.CloseTimestampsModalCommand.Execute(null);
                    e.Handled = true;
                }
                else if (viewModel.IsFixMatchOpen)
                {
                    viewModel.CloseFixMatchCommand.Execute(null);
                    e.Handled = true;
                }
            }
        };
    }

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender && DataContext is DetailViewModel vm)
        {
            vm.CloseFixMatchCommand.Execute(null);
        }
    }

    private void TimestampsBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender && DataContext is DetailViewModel vm)
        {
            vm.CloseTimestampsModalCommand.Execute(null);
        }
    }

    private void Modal_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }
}
