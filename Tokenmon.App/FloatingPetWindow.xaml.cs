using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Tokenmon.App;

public partial class FloatingPetWindow : Window
{
    public FloatingPetWindow(MainViewModel viewModel, Action showMain)
    {
        ViewModel = viewModel;
        ShowMain = showMain;
        DataContext = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
    public Action ShowMain { get; }
    public System.Windows.Point DragStart { get; private set; }
    public bool IsDragging { get; private set; }
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void PetMouseDown(object sender, MouseButtonEventArgs e)
    {
        DragStart = e.GetPosition(this);
        IsDragging = false;
        CaptureMouse();
    }

    private void PetMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!IsDragging && (position - DragStart).Length < 4)
        {
            return;
        }

        IsDragging = true;
        var screen = PointToScreen(position);
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = screen.X / dpi.DpiScaleX - DragStart.X;
        Top = screen.Y / dpi.DpiScaleY - DragStart.Y;
    }

    private async void PetMouseUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (!IsDragging)
        {
            ShowMain();
            return;
        }

        ViewModel.Settings.PetLeft = Left;
        ViewModel.Settings.PetTop = Top;
        await ViewModel.SaveSettings();
    }

    private void PetDoubleClick(object sender, MouseButtonEventArgs e) => ShowMain();
}
