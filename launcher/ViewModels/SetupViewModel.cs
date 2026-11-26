using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ben10Launcher.Services;

namespace Ben10Launcher.ViewModels;

/// <summary>One line of the build's step list.</summary>
public partial class SetupStep : ObservableObject
{
    public SetupStep(string label, double weight, string note)
    {
        Label = label;
        Weight = weight;
        Note = note;
    }

    public string Label { get; }
    [ObservableProperty] private string _note;   // about how long it takes (or, while it runs, what it is doing)
    public double Weight { get; }  // its share of the whole
    [ObservableProperty] private string _state = "next"; // next | now | done
    public string Mark => State switch { "done" => "✓", "now" => "●", _ => "○" };
    public bool IsDone => State == "done";
    public bool IsNow => State == "now";
    public bool IsNext => State == "next";
    partial void OnStateChanged(string value)
    {
        OnPropertyChanged(nameof(Mark));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsNow));
        OnPropertyChanged(nameof(IsNext));
    }
}

/// <summary>The setup's output (tools\setup_game.py), shown in plain words under the progress bar: null = keep the
/// line already shown.</summary>
public static class SetupOutput
{
    private static readonly System.Text.RegularExpressions.Regex Ninja = new(@"^\[(\d+)/(\d+)\]");
    private static readonly System.Text.RegularExpressions.Regex Sectors = new(@"^(\d+)/(\d+) sectors$");

    /// <summary>Only lines written for people: errors come as [error] lines and are shown on their own, so the build's
    /// other output (paths, CMake's checks and warnings, listings) stays in the log.</summary>
    public static string? Friendly(string line)
    {
        line = line.Trim();
        if (line.StartsWith("[retry] "))
            return L.T("The build stopped. Trying again, more slowly");
        if (line.StartsWith("-- "))
            return L.T("Getting the build ready");
        if (Ninja.Match(line) is { Success: true } m && m.Groups[1].Value != "0")
            return L.F("Building: part {0} of {1}", m.Groups[1].Value, m.Groups[2].Value);
        if (line.StartsWith("checked ") && line.Contains(" of "))
            return L.F("Checking which files changed: {0} files", line[8..]);
        if (line == "Turning the game's code into C++" || line == "Unpacking the Windows SDK" ||
            line == "Reading Microsoft's list of the C++ tools")
            return L.T(line);
        // the portable C++ tools (tools/portable_msvc.py): "Getting the C++ tools: N of T MB"
        if (line.StartsWith("Getting the C++ tools: "))
            return L.F("Getting the C++ tools: {0}", line["Getting the C++ tools: ".Length..]);
        if (line.StartsWith("Microsoft's C++ build tools are missing"))
            return line;
        // the build tools too old or without the Windows SDK: why Microsoft's installer runs ("<why>: downloading
        // Microsoft's installer (...)")
        if (line.IndexOf(": downloading Microsoft's installer", StringComparison.Ordinal) is var cut and > 0)
            return char.ToUpperInvariant(line[0]) + line[1..cut] + ". Microsoft's installer runs (a Windows prompt asks to allow it)";
        if (line == "the disc is already prepared")
            return L.T("Your disc was already read");
        if (Sectors.Match(line) is { Success: true } d && long.TryParse(d.Groups[2].Value, out var all) && all > 0)
            return L.F("Reading your disc: {0}%", 100 * long.Parse(d.Groups[1].Value) / all);
        return null;
    }

    /// <summary>The error, then the tool's own error lines (the [detail] lines before it): what a player's screenshot
    /// of the error needs to show.</summary>
    public static string WithDetails(string error, IReadOnlyList<string> details) =>
        details.Count == 0 ? error : error + "\n\n" + L.T("What the build said:") + "\n" + string.Join("\n", details);
}

/// <summary>The time line under the progress bar. A time left only while the game is built, from that step's own pace
/// (it is most of a first setup and has real progress); before that the steps' lengths vary too much to guess, so
/// how long it has been running.</summary>
public static class SetupTime
{
    public static string Line(TimeSpan elapsed, bool building, double buildProgress, TimeSpan buildElapsed)
    {
        if (building && buildProgress >= 0.25 && buildElapsed.TotalSeconds >= 30)
        {
            var left = buildElapsed.TotalSeconds * (1 - buildProgress) / buildProgress;
            var minutes = Math.Max(1, (int)Math.Round(left / 60));
            return minutes == 1 ? L.T("About 1 minute left") : L.F("About {0} minutes left", minutes);
        }
        var so = (int)elapsed.TotalMinutes;
        return so < 1 ? L.T("Under a minute so far") : L.F("{0} min so far", so);
    }
}

public partial class SetupViewModel : ObservableObject
{
    private readonly GameFolder _folder;
    private readonly Action _finished;
    private Process? _process;
    private bool _cancelled;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _stepClock = new(); // since the current step began (the build's pace)
    private StreamWriter? _log;

    public SetupViewModel(GameFolder folder, Action finished)
    {
        _folder = folder;
        _finished = finished;
        // the steps tools\setup_game.py goes through, with about how much of the time each takes
        Steps = new ObservableCollection<SetupStep>
        {
            new(L.T("Reading your disc"), 0.03, L.T("about 1 min")),
            new(L.T("Getting the build tools"), 0.08, L.T("about 1 GB, only if missing")),
            new(L.T("Building the recompiler"), 0.05, L.T("about 3 min")),
            new(L.T("Turning the game's code into C++"), 0.06, L.T("about 2 min")),
            new(L.T("Building the game"), 0.73, L.T("about 30-60 min")),
            new(L.T("Preparing graphics for your PC"), 0.04, L.T("about 2 min")),
            new(L.T("Finishing"), 0.01, ""),
        };
    }

    public ObservableCollection<SetupStep> Steps { get; }

    // --- step 1: the disc ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscPage), nameof(IsBuildPage), nameof(BuildingNow))]
    private int _page = 1;

    public bool IsDiscPage => Page == 1;
    public bool IsBuildPage => Page == 2;
    public bool BuildingNow => IsBuildPage && Building;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisc), nameof(CanBuild), nameof(DiscAlreadyRead))]
    private string? _discPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuild), nameof(ShowDiscOk), nameof(ShowDiscBad))]
    private bool _discOk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuild))]
    private bool _checking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDiscBad))]
    private string _discProblem = "";

    [ObservableProperty] private bool _makeShortcuts = true;

    public bool HasDisc => DiscPath != null;
    public bool ShowDiscOk => DiscOk && !Checking;
    public bool ShowDiscBad => DiscProblem.Length > 0 && !Checking;
    // a disc read in an earlier attempt (game\ is there) needs no new one
    public bool CanBuild => !Checking && !FolderTooLong && !FolderInOneDrive && (DiscOk || (DiscPath == null && _folder.HasGame));

    // The build's deepest files lie ~140 characters below this folder, and the compiler can't write past Windows'
    // 260-character path limit ("cannot open program database"): a folder deep inside others has to move first.
    public const int MaxFolderLength = 100;
    public bool FolderTooLong => _folder.Root.Length > MaxFolderLength;
    // OneDrive would upload the build's ~10 GB while it is made, and holds files the compiler is writing: the setup
    // stops halfway. Desktops and Documents are often inside OneDrive without the player knowing.
    public bool FolderInOneDrive =>
        new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .Any(d => !string.IsNullOrEmpty(d) &&
                      _folder.Root.StartsWith(d.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) ||
        _folder.Root.Contains(@"\OneDrive\", StringComparison.OrdinalIgnoreCase);
    public bool DiscAlreadyRead => DiscPath == null && _folder.HasGame;
    public string FolderPath => _sample ? @"C:\Jogos\Ben 10 Recomp" : _folder.Root;
    private bool _sample; // the README's pictures (--shot setup-found): no folder of this PC shown
    public string SpaceText
    {
        get
        {
            var free = _sample ? 120 : _folder.FreeSpaceGb;
            return free >= 0 ? L.F("About 15 GB needed · {0} GB free", free.ToString("0", CultureInfo.InvariantCulture)) : L.T("About 15 GB needed");
        }
    }

    partial void OnCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDiscOk));
        OnPropertyChanged(nameof(ShowDiscBad));
    }

    // --- the disc found by itself (DiscFinder): run when the first launch opens, and by "Search this PC" ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowScanText))]
    private bool _scanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowScanText))]
    private string _scanText = "";

    public bool ShowScanText => ScanText.Length > 0;
    private CancellationTokenSource? _scan;

    [RelayCommand]
    private Task Scan() => ScanAsync();

    public async Task ScanAsync()
    {
        _scan?.Cancel();
        var cancel = (_scan = new CancellationTokenSource()).Token;
        Scanning = true;
        ScanText = L.T("Looking for your disc on this PC…");
        var candidates = await Task.Run(() => DiscFinder.Find(cancel));
        foreach (var iso in candidates.Take(40))
        {
            if (cancel.IsCancellationRequested)
                return;
            if (await _folder.CheckDiscAsync(iso) == null && !cancel.IsCancellationRequested)
            {
                Scanning = false;
                ScanText = L.T("Found it on your PC.");
                await SetDiscAsync(iso, fromScan: true);
                return;
            }
        }
        if (cancel.IsCancellationRequested)
            return;
        Scanning = false;
        ScanText = L.T("Couldn't find it on this PC. Choose the file with Browse…, or drop it here.");
    }

    public Task SetDiscAsync(string path) => SetDiscAsync(path, fromScan: false);

    private async Task SetDiscAsync(string path, bool fromScan)
    {
        if (!fromScan)
        {
            _scan?.Cancel(); // the player chose one: the search stops
            Scanning = false;
            ScanText = "";
        }
        DiscPath = path;
        DiscOk = false;
        DiscProblem = "";
        Checking = true;
        var problem = await _folder.CheckDiscAsync(path);
        Checking = false;
        DiscOk = problem == null;
        DiscProblem = problem ?? "";
    }

    // --- step 2: building ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText), nameof(BarWidth))]
    private double _percent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Building), nameof(Title), nameof(BuildingNow))]
    private bool _done;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Building), nameof(HasError), nameof(BuildingNow), nameof(Title))]
    private string _error = "";

    [ObservableProperty] private string _timeLine = "";
    [ObservableProperty] private string _lastLine = "";

    public bool Building => !Done && Error.Length == 0;
    public bool HasError => Error.Length > 0;

    /// <summary>A shortcut that couldn't be made (the game is built and plays all the same).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutProblem))]
    private string _shortcutProblem = "";

    public bool HasShortcutProblem => ShortcutProblem.Length > 0;
    public string PercentText => $"{Math.Round(Percent * 100)}%";
    public double BarWidth => Percent; // 0-1, the view scales it
    public string Title => L.T(Done ? "The game is built" : HasError ? "The setup stopped" : "Building the game from your disc");
    public string LogPath => Path.Combine(_folder.LogsPath, "setup.log");

    [RelayCommand]
    private void Build()
    {
        if (!CanBuild)
            return;
        Page = 2;
        Start();
    }

    private void Start()
    {
        foreach (var s in Steps)
            s.State = "next";
        Percent = 0;
        Done = false;
        Error = "";
        TimeLine = L.T("Starting…");
        LastLine = "";
        _cancelled = false;
        Directory.CreateDirectory(_folder.LogsPath);
        _log = GameFolder.OpenRunLog(LogPath);
        _details.Clear();
        _clock.Restart();
        try
        {
            _process = Process.Start(_folder.SetupStart(DiscOk ? DiscPath : null));
        }
        catch (Exception ex)
        {
            Fail(L.F("Couldn't start the setup: {0}", ex.Message));
            return;
        }
        if (_process == null)
        {
            Fail(L.T("Couldn't start the setup."));
            return;
        }
        var process = _process;
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
            {
                var text = line;
                Dispatcher.UIThread.Post(() => OnLine(text));
            }
        });
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
            {
                var text = line;
                Dispatcher.UIThread.Post(() => OnLine(text));
            }
        });
        _ = Task.Run(async () =>
        {
            await process.WaitForExitAsync();
            await Task.Delay(300); // the last lines first
            Dispatcher.UIThread.Post(() => OnExit(process.ExitCode));
        });
    }

    private int _step = -1;
    private double _stepProgress;

    private void OnLine(string line)
    {
        _log?.WriteLine(line);
        if (line.StartsWith("[step] "))
        {
            var parts = line.Split(' ', 4);
            if (int.TryParse(parts[1], out var n))
            {
                for (int i = 0; i < Steps.Count; i++)
                    Steps[i].State = i < n - 1 ? "done" : (i == n - 1 ? "now" : "next");
                _step = n - 1;
                ToolsNow = n == 2;
                if (!ToolsNow)
                    InstallerOpening = false;
                _stepProgress = 0;
                _stepClock.Restart();
                LastLine = "";
                UpdatePercent();
            }
            return;
        }
        if (line.StartsWith("[progress] "))
        {
            if (double.TryParse(line[11..], NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            {
                _stepProgress = p;
                UpdatePercent();
            }
            return;
        }
        if (line.StartsWith("[detail] "))
        {
            _details.Add(line[9..]);
            return;
        }
        if (line.StartsWith("[error] "))
        {
            Fail(SetupOutput.WithDetails(line[8..], _details));
            return;
        }
        // the plan B: Microsoft's installer is about to open (its window and a Windows prompt)
        if (line.Contains(": downloading Microsoft's installer", StringComparison.Ordinal))
            InstallerOpening = true;
        if (SetupOutput.Friendly(line) is { } shown)
            LastLine = shown;
    }

    // --- the notice while the C++ tools are got (step 2): what happens, and what to do if Windows asks ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowToolsNotice), nameof(ShowInstallerNotice))]
    private bool _toolsNow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowToolsNotice), nameof(ShowInstallerNotice))]
    private bool _installerOpening;

    public bool ShowToolsNotice => ToolsNow && !InstallerOpening && Building;
    public bool ShowInstallerNotice => InstallerOpening && Building;

    private readonly List<string> _details = new(); // the tool's own error lines, shown under the error

    private void UpdatePercent()
    {
        double before = 0;
        for (int i = 0; i < _step && i < Steps.Count; i++)
            before += Steps[i].Weight;
        var total = before + (_step >= 0 && _step < Steps.Count ? Steps[_step].Weight * _stepProgress : 0);
        Percent = Math.Clamp(total, 0, 1);
        if (Percent < 1)
            TimeLine = SetupTime.Line(_clock.Elapsed, _step == 4, _stepProgress, _stepClock.Elapsed);
    }

    private void OnExit(int code)
    {
        _log?.Dispose();
        _log = null;
        _process = null;
        if (_cancelled)
            return;
        if (code != 0 || Error.Length > 0)
        {
            if (Error.Length == 0)
                Fail(L.F("The setup stopped (exit code {0}).", code));
            return;
        }
        foreach (var s in Steps)
            s.State = "done";
        Percent = 1;
        // the game's shortcut in the install folder always, the desktop ones if asked for
        ShortcutProblem = _folder.CreateShortcuts(MakeShortcuts) ?? "";
        TimeLine = L.F("Done in {0} minutes", Math.Max(1, (int)Math.Round(_clock.Elapsed.TotalMinutes)));
        Done = true;
    }

    private void Fail(string why)
    {
        ToolsNow = false;
        InstallerOpening = false;
        Error = why;
        TimeLine = "";
        foreach (var s in Steps.Where(s => s.State == "now"))
            s.State = "next";
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancelled = true;
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process = null;
        _log?.Dispose();
        _log = null;
        Error = "";
        Page = 1;
    }

    [RelayCommand]
    private void TryAgain() => Start();

    [RelayCommand]
    private void Back()
    {
        Error = "";
        Page = 1;
    }

    /// <summary>Step 3: on to the settings.</summary>
    [RelayCommand]
    private void Continue() => _finished();

    [RelayCommand]
    private void OpenLog() => GameFolder.OpenText(LogPath);

    /// <summary>For the README's pictures (--shot setup-found): the disc found by the search, as on a player's PC.</summary>
    public void ShowSample()
    {
        _sample = true;
        DiscPath = @"C:\Users\Jogador\Downloads\Ben 10 - Protector of Earth.iso";
        DiscOk = true;
        ScanText = L.T("Found it on your PC.");
        OnPropertyChanged(nameof(FolderPath));
        OnPropertyChanged(nameof(SpaceText));
    }

    /// <summary>For pictures (--shot setup-installer / setup-tools): step 2, with Microsoft's installer opening or not.</summary>
    public void ShowToolsSample(bool installer)
    {
        _sample = true;
        Page = 2;
        _step = 1;
        for (int i = 0; i < Steps.Count; i++)
            Steps[i].State = i < 1 ? "done" : (i == 1 ? "now" : "next");
        Percent = 0.05;
        TimeLine = L.T("Under a minute so far");
        ToolsNow = true;
        InstallerOpening = installer;
    }
}
