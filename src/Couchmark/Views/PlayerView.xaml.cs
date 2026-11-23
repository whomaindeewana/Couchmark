using Couchmark.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Couchmark.Views;

/// <summary>Code-behind for PlayerView.xaml. LibVLC wired up in Phase 4.</summary>
public partial class PlayerView : Page
{
    private readonly System.Windows.Threading.DispatcherTimer _mouseTimer;
    private WindowState _prevWindowState = WindowState.Normal;
    private WindowStyle _prevWindowStyle = WindowStyle.SingleBorderWindow;

    public PlayerView(PlayerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Focusable = true;
        Loaded += (s, e) => Focus();
        Unloaded += (s, e) =>
        {
            if (DataContext is PlayerViewModel vm && vm.IsFullscreen)
            {
                vm.IsFullscreen = false;
            }
        };

        _mouseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _mouseTimer.Tick += (s, e) =>
        {
            if (DataContext is PlayerViewModel vm && vm.IsPlaying)
            {
                vm.ControlsVisible = false;
                Cursor = System.Windows.Input.Cursors.None;
            }
            _mouseTimer.Stop();
        };

        MouseMove += (s, e) =>
        {
            if (DataContext is PlayerViewModel vm)
            {
                vm.ControlsVisible = true;
                Cursor = System.Windows.Input.Cursors.Arrow;
                _mouseTimer.Stop();
                _mouseTimer.Start();
            }
        };

        ProgressSlider.PreviewMouseDown += (s, e) =>
        {
            if (DataContext is PlayerViewModel vm)
            {
                vm.IsUserSeeking = true;
            }
        };

        ProgressSlider.PreviewMouseUp += (s, e) =>
        {
            if (DataContext is PlayerViewModel vm)
            {
                vm.SeekTo((long)ProgressSlider.Value);
                vm.IsUserSeeking = false;
            }
        };

        PreviewKeyDown += PlayerView_PreviewKeyDown;
    }

    private void PlayerView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm) return;

        switch (e.Key)
        {
            case System.Windows.Input.Key.Space:
                vm.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.M:
                vm.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Left:
                vm.SeekRelative(-10);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Right:
                vm.SeekRelative(10);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Up:
                vm.Volume = Math.Min(100, vm.Volume + 5);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Down:
                vm.Volume = Math.Max(0, vm.Volume - 5);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.F:
                vm.ToggleFullscreenCommand.Execute(null);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.S:
                if (vm.IsSkipIntroVisible)
                {
                    vm.SkipIntroCommand.Execute(null);
                    e.Handled = true;
                }
                else if (vm.IsSkipOutroVisible)
                {
                    vm.SkipOutroCommand.Execute(null);
                    e.Handled = true;
                }
                break;
            case System.Windows.Input.Key.T:
                if (vm.IsTimestampsDialogOpen)
                    vm.CloseTimestampsDialogCommand.Execute(null);
                else
                    vm.OpenTimestampsDialogCommand.Execute(null);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Escape:
                if (vm.IsTimestampsDialogOpen)
                {
                    vm.CloseTimestampsDialogCommand.Execute(null);
                }
                else if (vm.IsFullscreen)
                {
                    vm.ToggleFullscreenCommand.Execute(null);
                }
                else
                {
                    vm.StopCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }

        vm.ControlsVisible = true;
        Cursor = System.Windows.Input.Cursors.Arrow;
        _mouseTimer.Stop();
        _mouseTimer.Start();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsFullscreen))
        {
            var vm = (PlayerViewModel)DataContext;
            var window = Window.GetWindow(this);
            if (window != null)
            {
                if (vm.IsFullscreen)
                {
                    _prevWindowState = window.WindowState;
                    _prevWindowStyle = window.WindowStyle;
                    window.WindowStyle = WindowStyle.None;
                    window.WindowState = WindowState.Maximized;
                }
                else
                {
                    window.WindowStyle = _prevWindowStyle;
                    window.WindowState = _prevWindowState;
                }
            }
        }
    }
}
