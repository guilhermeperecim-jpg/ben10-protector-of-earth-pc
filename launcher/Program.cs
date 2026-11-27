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
        // Dev: a picture made into the shortcut's icon (IconMaker): --make-icon <picture> <out.ico>
        if (args.Length >= 3 && args[0] == "--make-icon")
        {
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
            IconMaker.Make(args[1], args[2]);
            Console.WriteLine("icon: " + args[2]);
            return;
        }
        // RecompLauncher (recomplauncher.json): the first-launch setup without a window, and Play straight into the game
        if (args.Length >= 1 && args[0] == "--prepare")
        {
            if (!args.Contains("--accept-vs-license"))
            {
                // the build downloads Microsoft's C++ tools: only the player accepts their license (RecompLauncher's tick)
                Console.WriteLine("Building the game needs Microsoft's C++ tools: accept their license (--accept-vs-license).");
                Environment.ExitCode = 4;
                return;
            }
            Environment.ExitCode = Prepare(args.FirstOrDefault(a => a.StartsWith("--disc="))?["--disc=".Length..]);
            return;
        }
        if (args.Length >= 1 && args[0] == "--play")
        {
            Environment.ExitCode = PlayAndWait();
            return;
        }
        // --update: open on the new version's notes (the game's update popup)
        ViewModels.UpdateViewModel.OpenOnUpdate = args.Contains("--update");
        ViewModels.UpdateViewModel.OpenOnRebuild = args.Contains("--rebuild");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    /// <summary>tools\setup_game.py as the first launch runs it, with its steps printed as RecompLauncher's
    /// "progress &lt;0-1&gt; &lt;text&gt;" lines and the reason of a failure printed last. Only the portable C++ tools:
    /// with no window, Microsoft's installer can't be offered.</summary>
    private static int Prepare(string? disc)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        L.Use("auto");
        var folder = GameFolder.Find();
        if (FolderCheck.Problems(folder) is { Count: > 0 } problems)
        {
            Console.WriteLine($"{problems[0].Title}: {problems[0].Detail}");
            return 3;
        }
        var start = folder.SetupStart(disc);
        start.ArgumentList.Add("--portable-tools");
        Directory.CreateDirectory(folder.LogsPath);
        using var log = GameFolder.OpenRunLog(Path.Combine(folder.LogsPath, "setup.log"));
        using var setup = System.Diagnostics.Process.Start(start)!;
        var errors = setup.StandardError.ReadToEndAsync();
        int step = 1, steps = 1;
        string title = "", reason = "";
        void Progress(double within) => Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"progress {Math.Clamp((step - 1 + within) / steps, 0, 1):0.###} {title}"));
        while (setup.StandardOutput.ReadLine() is { } line)
        {
            log.WriteLine(line);
            var parts = line.Split(' ', 4);
            if (parts[0] == "[step]" && parts.Length == 4 && int.TryParse(parts[1], out step) && int.TryParse(parts[2], out steps))
            {
                title = parts[3];
                Progress(0);
            }
            else if (parts[0] == "[progress]" && double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out var within))
                Progress(within);
            else if (line.StartsWith("[error] "))
                reason = line[8..];
            else if (line != "[done]")
                Console.WriteLine(line);
        }
        setup.WaitForExit();
        var stderr = errors.Result.TrimEnd();
        if (stderr.Length > 0)
        {
            log.WriteLine(stderr);
            Console.WriteLine(stderr);
        }
        if (setup.ExitCode != 0 && reason.Length > 0)
            Console.WriteLine(reason); // last: the line a player reads
        return setup.ExitCode;
    }

    /// <summary>The built game, started as the PLAY button does, waited for (RecompLauncher backs the saves up when it
    /// closes) and its exit code passed on.</summary>
    private static int PlayAndWait()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        L.Use("auto");
        var why = GameFolder.Find().Play(out var game);
        if (why != null || game == null)
        {
            Console.WriteLine(why ?? L.T("The game isn't built yet."));
            return 2;
        }
        game.WaitForExit();
        return game.ExitCode;
    }

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
