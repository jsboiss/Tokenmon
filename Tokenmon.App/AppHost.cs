using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Tokenmon.Core;
using Forms = System.Windows.Forms;

namespace Tokenmon.App;

public sealed class AppHost : IDisposable
{
    public AppHost()
    {
        InstanceMutex = new Mutex(true, "Local\\Tokenmon.SingleInstance", out var isFirstInstance);
        IsFirstInstance = isFirstInstance;

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tokenmon");
        HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Tokenmon/0.1");
        StateStore = new JsonStateStore(appData);
        PokemonClient = new PokemonClient(HttpClient, Path.Combine(appData, "sprites"));
        ViewModel = new MainViewModel(
            new LocalUsageService(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            new CodexRateLimitService(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            new CompanionEngine(PokemonClient),
            StateStore);
        Window = new MainWindow(ViewModel);
        PetWindow = new FloatingPetWindow(ViewModel, ShowFlyout);
        TrayIcon = new Forms.NotifyIcon
        {
            Icon = IconFactory.CreatePokeball(),
            Text = "Tokenmon",
            Visible = true,
            ContextMenuStrip = CreateMenu()
        };
        RefreshTimer = new DispatcherTimer();
    }

    public Mutex InstanceMutex { get; }
    public bool IsFirstInstance { get; }
    public HttpClient HttpClient { get; }
    public JsonStateStore StateStore { get; }
    public PokemonClient PokemonClient { get; }
    public MainViewModel ViewModel { get; }
    public MainWindow Window { get; }
    public FloatingPetWindow PetWindow { get; }
    public Forms.NotifyIcon TrayIcon { get; }
    public DispatcherTimer RefreshTimer { get; }
    public bool IsDisposed { get; private set; }

    public void Start()
    {
        if (!IsFirstInstance)
        {
            System.Windows.MessageBox.Show(
                "Tokenmon is already running in the notification area.",
                "Tokenmon",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            System.Windows.Application.Current.Shutdown();
            return;
        }

        TrayIcon.MouseClick += TrayMouseClick;
        ViewModel.PropertyChanged += ViewModelPropertyChanged;
        ViewModel.SettingsChanged += ViewModelSettingsChanged;
        ViewModel.CompanionEvent += ViewModelCompanionEvent;
        RefreshTimer.Tick += RefreshTimerTick;
        ApplySettings();
        RefreshTimer.Start();
        _ = Refresh();
        var arguments = Environment.GetCommandLineArgs();
        if (arguments.Contains("--show", StringComparer.OrdinalIgnoreCase) ||
            !arguments.Contains("--startup", StringComparer.OrdinalIgnoreCase))
        {
            ShowFlyout();
        }
    }

    public void ShowFlyout()
    {
        var workArea = SystemParameters.WorkArea;
        Window.Left = workArea.Right - Window.Width - 8;
        Window.Top = workArea.Bottom - Window.Height - 8;
        Window.Show();
        Window.Activate();
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        RefreshTimer.Stop();
        TrayIcon.Visible = false;
        TrayIcon.Dispose();
        HttpClient.Dispose();
        Window.AllowClose = true;
        PetWindow.AllowClose = true;
        Window.Close();
        PetWindow.Close();
        if (IsFirstInstance)
        {
            InstanceMutex.ReleaseMutex();
        }

        InstanceMutex.Dispose();
    }

    private Forms.ContextMenuStrip CreateMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        var open = new Forms.ToolStripMenuItem("Open Tokenmon");
        var refresh = new Forms.ToolStripMenuItem("Refresh usage");
        var pet = new Forms.ToolStripMenuItem("Show floating companion")
        {
            Checked = ViewModel.Settings.ShowFloatingPet,
            CheckOnClick = true
        };
        var exit = new Forms.ToolStripMenuItem("Exit");
        open.Click += OpenMenuClick;
        refresh.Click += RefreshMenuClick;
        pet.Click += PetMenuClick;
        exit.Click += ExitMenuClick;
        menu.Items.Add(open);
        menu.Items.Add(refresh);
        menu.Items.Add(pet);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private void ApplySettings()
    {
        RefreshTimer.Interval = TimeSpan.FromMinutes(Math.Clamp(ViewModel.Settings.RefreshMinutes, 1, 15));
        StartupService.SetEnabled(ViewModel.Settings.StartWithWindows);
        PetWindow.Width = ViewModel.Settings.PetSize;
        PetWindow.Height = ViewModel.Settings.PetSize;

        if (!double.IsNaN(ViewModel.Settings.PetLeft) && !double.IsNaN(ViewModel.Settings.PetTop))
        {
            PetWindow.Left = ViewModel.Settings.PetLeft;
            PetWindow.Top = ViewModel.Settings.PetTop;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            PetWindow.Left = workArea.Right - PetWindow.Width - 28;
            PetWindow.Top = workArea.Bottom - PetWindow.Height - 64;
        }

        if (ViewModel.Settings.ShowFloatingPet)
        {
            PetWindow.Show();
        }
        else
        {
            PetWindow.Hide();
        }

        if (TrayIcon.ContextMenuStrip?.Items[2] is Forms.ToolStripMenuItem petItem)
        {
            petItem.Checked = ViewModel.Settings.ShowFloatingPet;
        }
    }

    private async Task Refresh()
    {
        await ViewModel.Refresh();
        UpdateTray();
    }

    private void UpdateTray()
    {
        var text = $"{ViewModel.CompanionName} · Today {ViewModel.TodayTokens} tokens";
        TrayIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void TrayMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            ShowFlyout();
        }
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.TodayTokens) or nameof(MainViewModel.CompanionName))
        {
            UpdateTray();
        }
    }

    private void ViewModelSettingsChanged(object? sender, EventArgs e) => ApplySettings();

    private void ViewModelCompanionEvent(object? sender, string message)
    {
        TrayIcon.BalloonTipTitle = "Tokenmon";
        TrayIcon.BalloonTipText = message;
        TrayIcon.ShowBalloonTip(5_000);
    }

    private async void RefreshTimerTick(object? sender, EventArgs e) => await Refresh();

    private void OpenMenuClick(object? sender, EventArgs e) => ShowFlyout();

    private async void RefreshMenuClick(object? sender, EventArgs e) => await Refresh();

    private async void PetMenuClick(object? sender, EventArgs e)
    {
        if (sender is Forms.ToolStripMenuItem item)
        {
            ViewModel.Settings.ShowFloatingPet = item.Checked;
            await ViewModel.SaveSettings();
        }
    }

    private void ExitMenuClick(object? sender, EventArgs e) => System.Windows.Application.Current.Shutdown();
}
