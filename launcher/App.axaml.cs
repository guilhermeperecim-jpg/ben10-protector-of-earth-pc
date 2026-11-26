using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ben10Launcher.Services;
using Ben10Launcher.ViewModels;
using Ben10Launcher.Views;

namespace Ben10Launcher;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = Make(GameFolder.Find(), page: "");
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The window, in the language launcher.toml says, on a page ("" = home).</summary>
    private static MainWindow Make(GameFolder folder, string page)
    {
        L.Load(folder);
        var model = new MainViewModel(folder);
        if (page.Length > 0)
            model.ShowPage(page);
        var window = new MainWindow { DataContext = model };
        // a new language: the window made again in it, on the same page
        model.Settings.LanguageChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;
            var old = desktop.MainWindow;
            var fresh = Make(folder, "options");
            fresh.Position = old?.Position ?? fresh.Position;
            fresh.Width = old?.Width ?? fresh.Width;
            fresh.Height = old?.Height ?? fresh.Height;
            desktop.MainWindow = fresh;
            fresh.Show();
            old?.Close();
        });
        return window;
    }
}
