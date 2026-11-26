using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Ben10Launcher.Services;
using Ben10Launcher.ViewModels;
using Ben10Launcher.Views;

namespace Ben10Launcher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Dev: a page as a picture, drawn without opening a window (nothing is saved):
        //   "Ben 10 Recomp Launcher" --shot <home|options|controls|files|setup> <out.png> [pt|en] [width height]
        if (args.Length >= 3 && args[0] == "--shot")
        {
            Shot(args[1], args[2], args.Length >= 4 ? args[3] : "auto",
                 args.Length >= 6 ? int.Parse(args[4]) : 1280, args.Length >= 6 ? int.Parse(args[5]) : 760);
            return;
        }
        // Dev / support: what the first launch's search finds (every .iso, likeliest first, and which is the right disc)
        if (args.Length >= 1 && args[0] == "--find-disc")
        {
            var folder = GameFolder.Find();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var found = DiscFinder.Find(CancellationToken.None);
            Console.WriteLine($"{found.Count} .iso found in {clock.Elapsed.TotalSeconds:0.0} s");
            foreach (var iso in found)
                Console.WriteLine((folder.CheckDiscAsync(iso).GetAwaiter().GetResult() is { } why ? "  no: " + why : "  YES") +
                                  "  " + iso);
            return;
        }
        // --update: open on the new version's notes (the game's update popup)
        ViewModels.UpdateViewModel.OpenOnUpdate = args.Contains("--update");
        ViewModels.UpdateViewModel.OpenOnRebuild = args.Contains("--rebuild");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    private static void Shot(string page, string outPath, string language, int width, int height)
    {
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var folder = GameFolder.Find();
        L.Use(language);
        Machine.Hidden = Environment.GetEnvironmentVariable("BEN10_SHOT_PRIVATE") == "1";
        var model = new MainViewModel(folder);
        model.ShowPage(page);
        var window = new MainWindow { DataContext = model, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(outPath);
        window.Close();
    }
}
