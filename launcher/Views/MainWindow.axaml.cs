using Avalonia.Controls;
using Ben10Launcher.ViewModels;

namespace Ben10Launcher.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                await vm.OnOpenedAsync();
        };
    }
}
