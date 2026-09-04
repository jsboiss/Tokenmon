using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Tokenmon.Core;

namespace Tokenmon.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
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

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Hide();

    private async void RefreshClick(object sender, RoutedEventArgs e) => await ViewModel.Refresh();

    private async void SettingChanged(object sender, RoutedEventArgs e) => await ViewModel.SaveSettings();

    private async void PetSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
        {
            await ViewModel.SaveSettings();
        }
    }

    private async void UseCandyClick(object sender, RoutedEventArgs e) => await ViewModel.UseRareCandy();

    private async void UseMintClick(object sender, RoutedEventArgs e) => await ViewModel.UseMint();

    private async void BuyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag } ||
            !Enum.TryParse<ShopProduct>(tag, out var product))
        {
            return;
        }

        if (product is ShopProduct.PokemonEgg or ShopProduct.UncommonEgg or ShopProduct.RareEgg)
        {
            var result = System.Windows.MessageBox.Show(
                "This sends off your current companion and clears its current growth. Continue?",
                "Buy a fresh egg",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await ViewModel.Buy(product);
    }
}
