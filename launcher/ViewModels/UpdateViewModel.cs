using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ben10Launcher.Services;

namespace Ben10Launcher.ViewModels;

/// <summary>One line of a release's notes: a heading, a point, or plain text.</summary>
public sealed record NoteLine(string Text, bool IsHeading, bool IsBullet)
{
    public bool IsText => !IsHeading && !IsBullet;
}

/// <summary>Updates: the notice on the Play page, the Updates part of About, and the update itself (download, put in
/// place, rebuild) with its pages: what's new, updating, updated, and couldn't finish.</summary>
public partial class UpdateViewModel : ObservableObject
{
    private readonly GameFolder _folder;
    private readonly Func<bool> _checkOnOpen;
    private readonly Action _quit;
    private readonly Stopwatch _clock = new();
    private Release? _release;
    private CancellationTokenSource? _cancel;
    private Process? _rebuild;
    private int _setupStep = -1;
    private double _setupProgress;

    // the setup's 8 steps (tools\setup_game.py) within L.T("Rebuilding the game"): the disc is already read, the tools are
    // there, so the recompiler, the recompile and above all the game's build take the time
    // weights from a real update (0.1.0 to 0.1.1, 2026-10-12): the steps before ~0.5 min, turning the code into C++ and
    // checking which files changed ~6.5 min, the build ~1 min (longer when more changed); the graphics (a few seconds
    // when the cache still fits), the new arenas, wrestlers and songs (a copy) and the finish are short
    private static readonly double[] SetupWeights = { 0.01, 0.02, 0.04, 0.58, 0.30, 0.03, 0.01, 0.01 };
    private static readonly string[] SetupParts =
    {
        "reading your disc", "the build tools", "the recompiler", "turning the game's code into C++ and checking what changed",
        "building the game", "preparing graphics for your PC", "adding the new arenas, wrestlers and songs", "finishing",
    };
    private readonly Stopwatch _setupClock = new(); // since the rebuild's current part began

    public UpdateViewModel(GameFolder folder, Func<bool> checkOnOpen, Action quit)
    {
        _folder = folder;
        _checkOnOpen = checkOnOpen;
        _quit = quit;
        Steps = new ObservableCollection<SetupStep>
        {
            new("Downloading the new version", 0.12, ""),
            new("Checking the download", 0.02, ""),
            new("Putting the new files in place", 0.04, L.T("the old ones kept until it works")),
            new("Rebuilding the game", 0.80, L.T("a few minutes")),
            new("Finishing", 0.02, ""),
        };
    }

    public ObservableCollection<SetupStep> Steps { get; }
    public ObservableCollection<NoteLine> NoteLines { get; } = new();

    public string CurrentVersion => Updater.CurrentText;
    public bool CanUpdate => _sample || Updater.CanUpdate(_folder);
    private bool _sample; // the pictures of the pages (--shot) show what an install would

    // --- the page shown: none (the normal pages), notes (what's new), working, done, failed ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Active), nameof(IsNotes), nameof(IsWorking), nameof(IsDone), nameof(IsFailed),
        nameof(ShowNotice), nameof(ShowRebuildNotice), nameof(NavDownloadNow), nameof(NavDownloadDone), nameof(NavRebuildNow),
        nameof(NavRebuildDone), nameof(NavRebuildNext), nameof(NavDoneNow), nameof(NavDoneNext), nameof(Status))]
    private string _mode = "none";

    public bool Active => Mode != "none";
    public bool IsNotes => Mode == "notes";
    public bool IsWorking => Mode == "working";
    public bool IsDone => Mode == "done";
    public bool IsFailed => Mode == "failed";

    // --- the notice on Play ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNotice), nameof(ShowRebuildNotice), nameof(NewVersion), nameof(NoticeTitle),
        nameof(NoticePoints), nameof(NoticeMore), nameof(NoticeSize), nameof(NavTitle), nameof(NotesTitle))]
    private Release? _available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNotice), nameof(ShowRebuildNotice))]
    private bool _later;

    // --- the notice on Play when the game is older than its files (a release copied over the install by hand) ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRebuildNotice), nameof(RebuildTitle), nameof(RebuildText))]
    private string? _staleVersion; // the files' version, while the game was built from older ones

    private string? _staleBuilt;   // the version the game was built from (null: no record, built by 0.3.0 or earlier)
    private bool _afterCopy;       // the rebuild running is one after a copy by hand, not an update's

    /// <summary>Started by the game's own popup (--rebuild): the rebuild starts at once when the game needs it.</summary>
    public static bool OpenOnRebuild { get; set; }

    public bool ShowRebuildNotice => StaleVersion != null && !Active && !ShowNotice;
    public string RebuildTitle => L.T("The game needs rebuilding");

    public string RebuildText =>
        $"Your files are version {StaleVersion}, but the game was built from {(_staleBuilt != null ? "version " + _staleBuilt : "an older version")}: " +
        "the new files were copied in without rebuilding it, so you are still playing the old one. Rebuild it once to play " +
        $"version {StaleVersion} ({(_staleBuilt != null ? L.T("a few minutes") : "about 10 to 15 minutes, as everything is built afresh")}). " +
        "Your saves and settings stay as they are.";

    public bool ShowNotice => Available != null && !Later && !Active && CanUpdate;
    public string NewVersion => Available?.Version.ToString(3) ?? _toVersion;
    public string NoticeTitle => L.F("Version {0} is available", NewVersion);
    public string NoticeSize =>
        Available is { ZipSize: > 0 } r
            ? $"About {Math.Round(r.ZipSize / 1e6).ToString(CultureInfo.InvariantCulture)} MB to download, then the game is rebuilt on your PC. Your saves and settings stay as they are."
            : L.T("A download, then the game is rebuilt on your PC. Your saves and settings stay as they are.");
    /// <summary>The notes' first few points, on one line.</summary>
    /// <summary>The notice's summary: the first 3 points that are news (not "Good to know"), each as its first sentence,
    /// one per line; the whole notes are under "What's new".</summary>
    public IReadOnlyList<string> NoticePoints => NewsPoints().Take(3).Select(FirstSentence).ToList();

    public string NoticeMore
    {
        get
        {
            var more = NewsPoints().Count() - 3;
            return Available == null ? "" : more > 0 ? L.F("and {0} more under What's new", more) : NoticePoints.Count == 0 ? L.T("See what's new.") : "";
        }
    }

    private IEnumerable<string> NewsPoints()
    {
        var section = "";
        foreach (var line in ParseNotes(Available?.Notes ?? ""))
        {
            if (line.IsHeading)
                section = line.Text;
            else if (line.IsBullet && !section.Equals("Good to know", StringComparison.OrdinalIgnoreCase))
                yield return line.Text;
        }
    }

    private static string FirstSentence(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 ? text[..(end + 1)] : text;
    }

    // --- the update's own page list (in the page list's place) ---
    private string _toVersion = "";
    public string NavTitle => L.F("UPDATE TO {0}", NewVersion);
    public string NotesTitle => L.F("What's new in {0}", NewVersion);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavDownloadNow), nameof(NavDownloadDone), nameof(NavRebuildNow), nameof(NavRebuildDone),
        nameof(NavRebuildNext), nameof(NavDoneNow), nameof(NavDoneNext))]
    private int _stage; // 0 download (steps 1-3), 1 rebuild (steps 4-5), 2 done

    public bool NavDownloadNow => IsWorking && Stage == 0;
    public bool NavDownloadDone => Stage > 0;
    public bool NavRebuildNow => IsWorking && Stage == 1;
    public bool NavRebuildDone => Stage > 1;
    public bool NavRebuildNext => Stage < 1;
    public bool NavDoneNow => IsDone;
    public bool NavDoneNext => !IsDone;

    // --- progress ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private double _percent;

    [ObservableProperty] private string _timeLine = "";
    [ObservableProperty] private string _lastLine = "";
    [ObservableProperty] private string _workingTitle = "";
    public string PercentText => $"{Math.Round(Percent * 100)}%";

    // --- couldn't finish ---
    [ObservableProperty] private string _failTitle = "";
    [ObservableProperty] private string _failReason = "";
    [ObservableProperty] private string _failAdvice = "";
    [ObservableProperty] private string _keptVersion = "";

    // --- updated ---
    [ObservableProperty] private string _doneTitle = "";

    /// <summary>The header's status while an update page is open.</summary>
    public string Status => Mode switch
    {
        "working" => L.T("Updating…"),
        "done" => L.T("Ready to play"),
        "failed" => L.T("The update didn't finish"),
        _ => "",
    };

    // --- About: Updates ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckButton), nameof(CanCheck))]
    private bool _checking;

    [ObservableProperty] private string _checkText = "";
    public string CheckButton => Checking ? L.T("Checking…") : L.T("Check for updates");
    public bool CanCheck => !Checking && !Active && CanUpdate;

    // ============================================================================================================

    /// <summary>Started by the game's update popup (--update): check at once and open on what's new.</summary>
    public static bool OpenOnUpdate { get; set; }

    /// <summary>When the window opens: an update left halfway is undone (or its rebuild carried on); otherwise, if
    /// asked to, GitHub is asked for a newer version.</summary>
    public async Task StartAsync(bool inSetup)
    {
        Updater.CleanUp(_folder);
        CheckText = CanUpdate ? L.F("Version {0}", CurrentVersion) : L.T("This copy is updated with git (a developer's copy).");
        var journal = Updater.LoadJournal(_folder);
        if (journal != null)
        {
            Resume(journal);
            return;
        }
        if (!inSetup && Updater.StaleGame(_folder) is { } stale)
        {
            _staleBuilt = stale.Built;
            StaleVersion = stale.Files;
            Updater.Log(_folder, $"the game was built from {stale.Built ?? "an older version"}, its files are {stale.Files}: a rebuild is offered");
            if (OpenOnRebuild)
            {
                RebuildNow();
                return;
            }
        }
        if (!inSetup && CanUpdate && OpenOnUpdate)
        {
            // the player pressed Update in the game's popup: straight to the new version's notes and its Update button
            await CheckAsync(quiet: false);
            if (Available != null && Available.Version > Updater.Current)
                ShowNotes();
            return;
        }
        // started by RecompLauncher: it brings the new releases itself, and two updaters on one folder fight
        if (!inSetup && CanUpdate && _checkOnOpen() && Environment.GetEnvironmentVariable("RECOMPLAUNCHER") != "1")
            await CheckAsync(quiet: true);
    }

    [RelayCommand]
    private Task CheckNow() => CheckAsync(quiet: false);

    private async Task CheckAsync(bool quiet)
    {
        if (Checking)
            return;
        Checking = true;
        CheckText = L.T("Checking…");
        try
        {
            var release = await Updater.CheckAsync();
            if (release != null && release.Version > Updater.Current)
            {
                Available = release;
                Later = false;
                CheckText = L.F("Version {0} is available.", NewVersion);
            }
            else
                CheckText = L.F("Up to date (version {0}). Checked today at {1}.", CurrentVersion, DateTime.Now.ToString("HH:mm"));
        }
        catch (Exception ex)
        {
            Updater.Log(_folder, "checking for an update failed: " + ex.Message);
            CheckText = quiet ? L.F("Version {0}", CurrentVersion) : L.T("Couldn't check: no connection to GitHub. Try again later.");
        }
        finally
        {
            Checking = false;
        }
    }

    [RelayCommand]
    private void LaterOn() => Later = true;

    [RelayCommand]
    private void ShowNotes()
    {
        FillNotes(Available?.Notes ?? "");
        Mode = "notes";
    }

    [RelayCommand]
    private void Back() => Mode = "none";

    [RelayCommand]
    private void OpenLog() => GameFolder.OpenText(Updater.LogPath(_folder));

    [RelayCommand]
    private void OpenThisVersion() => GameFolder.Open($"{Updater.ReleasesUrl}/tag/v{CurrentVersion}");

    [RelayCommand]
    private void OpenAllVersions() => GameFolder.Open(Updater.ReleasesUrl);

    [RelayCommand]
    private void Finished()
    {
        Mode = "none";
        Stage = 0;
    }

    // --- the update ---

    [RelayCommand]
    private async Task UpdateNow()
    {
        if (Available is not { } release)
            return;
        _afterCopy = false;
        _release = release;
        _toVersion = release.Version.ToString(3);
        foreach (var s in Steps)
            s.State = "next";
        Percent = 0;
        TimeLine = "";
        LastLine = "";
        Stage = 0;
        WorkingTitle = L.F("Updating to version {0}", _toVersion);
        Mode = "working";
        _clock.Restart();
        Updater.Log(_folder, $"update to {_toVersion} started ({release.ZipUrl})");
        try
        {
            if (GameFolder.IsGameRunning())
                throw new UpdateException("busy", L.T("The game is open. Close it, then press Update again."));
            if (_folder.FreeSpaceGb is >= 0 and < 1.5)
                throw new UpdateException("other", L.T("Not enough free space: the update needs about 1.5 GB while it runs."));

            SetStep(0);
            var zip = Updater.ZipPath(_folder);
            _cancel = new CancellationTokenSource();
            await Updater.DownloadAsync(release, zip, (done, total) => Dispatcher.UIThread.Post(() =>
            {
                StepProgress(total > 0 ? done / (double)total : 0);
                LastLine = total > 0 ? $"{done / 1e6:0.0} of {total / 1e6:0.0} MB" : $"{done / 1e6:0.0} MB";
            }), _cancel.Token);

            SetStep(1);
            LastLine = L.T("Checked: the download is complete.");
            SetStep(2);
            var restart = await Task.Run(() => Updater.Apply(_folder, zip, release,
                p => Dispatcher.UIThread.Post(() => StepProgress(p))));
            Updater.Log(_folder, "the new files are in place" + (restart ? "; starting the new settings program" : ""));
            if (restart)
            {
                // the new program carries on (it finds the journal: the rebuild)
                LastLine = L.T("Starting the new version of this program…");
                await Task.Delay(400);
                Updater.Restart(_folder);
                _quit();
                return;
            }
            StartRebuild();
        }
        catch (Exception ex)
        {
            Fail(ex as UpdateException ?? new UpdateException("other", ex.Message), journal: null);
        }
    }

    /// <summary>A journal from an earlier run: a rebuild to carry on, an update cut off halfway to undo, or one that
    /// was undone (by the new program, which then started this one) to report.</summary>
    private void Resume(UpdateJournal journal)
    {
        _toVersion = journal.To;
        FillNotes(journal.Notes);
        switch (journal.State)
        {
            case "rebuild":
                Updater.Log(_folder, $"carrying on: the rebuild for {journal.To}");
                foreach (var s in Steps.Take(3))
                    s.State = "done";
                WorkingTitle = L.F("Updating to version {0}", journal.To);
                Mode = "working";
                _clock.Restart();
                StartRebuild();
                break;
            case "applying":
                Updater.Log(_folder, "an update was cut off while its files were put in place: undoing it");
                var restart = Updater.RollBack(_folder, journal);
                journal.State = "failed";
                journal.FailKind = "interrupted";
                journal.FailReason = L.T("The update was cut off before it finished (the program closed or the PC turned off).");
                if (restart)
                {
                    Updater.SaveJournal(_folder, journal);
                    Updater.Restart(_folder);
                    _quit();
                    return;
                }
                Updater.DeleteJournal(_folder);
                ShowFailed(journal.FailKind, journal.FailReason, journal.From);
                break;
            default:
                Updater.DeleteJournal(_folder);
                ShowFailed(journal.FailKind, journal.FailReason, journal.From);
                break;
        }
    }

    /// <summary>The game rebuilt from the files in place (a release copied over the install by hand): the update's
    /// rebuild and finish, with nothing downloaded and nothing to put back.</summary>
    [RelayCommand]
    private void RebuildNow()
    {
        if (StaleVersion is not { } version)
            return;
        _afterCopy = true;
        _toVersion = version;
        foreach (var s in Steps)
            s.State = "next";
        Percent = 0;
        TimeLine = "";
        LastLine = "";
        WorkingTitle = L.F("Rebuilding the game for version {0}", version);
        Mode = "working";
        _clock.Restart();
        Updater.Log(_folder, $"rebuild for {version} started (the files were copied in by hand)");
        StartRebuild();
    }

    private void StartRebuild()
    {
        SetStep(3);
        Stage = 1;
        _setupStep = -1;
        _setupProgress = 0;
        Steps[3].Note = L.T("a few minutes");
        LastLine = L.T("Rebuilding the game with the new version…");
        if (GameFolder.IsGameRunning())
        {
            FailRebuild("The game is open. Close it, then open this program again to finish the update.");
            return;
        }
        try
        {
            _rebuild = Process.Start(_afterCopy ? _folder.RebuildAfterCopyStart() : _folder.SetupStart(null));
        }
        catch (Exception ex)
        {
            FailRebuild(L.F("Couldn't start the rebuild: {0}", ex.Message));
            return;
        }
        if (_rebuild == null)
        {
            FailRebuild("Couldn't start the rebuild.");
            return;
        }
        var process = _rebuild;
        var log = GameFolder.OpenRunLog(Path.Combine(_folder.LogsPath, "update-build.log"));
        string? error = null;
        var details = new List<string>();
        void Line(string line) => Dispatcher.UIThread.Post(() =>
        {
            log.WriteLine(line);
            if (line.StartsWith("[detail] "))
                details.Add(line[9..]);
            else if (line.StartsWith("[error] "))
                error = SetupOutput.WithDetails(line[8..], details);
            else
                OnSetupLine(line);
        });
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) != null)
                Line(line);
        });
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync()) != null)
                Line(line);
        });
        _ = Task.Run(async () =>
        {
            await process.WaitForExitAsync();
            await Task.Delay(300); // the last lines first
            Dispatcher.UIThread.Post(() =>
            {
                log.Dispose();
                _rebuild = null;
                if (process.ExitCode != 0 || error != null)
                    FailRebuild(error ?? L.F("The rebuild stopped (exit code {0}).", process.ExitCode));
                else
                    FinishUpdate();
            });
        });
    }

    private void OnSetupLine(string line)
    {
        if (line.StartsWith("[step] "))
        {
            var parts = line.Split(' ', 4);
            if (int.TryParse(parts[1], out var n))
            {
                _setupStep = n - 1;
                _setupProgress = 0;
                _setupClock.Restart();
                LastLine = "";
                if (_setupStep >= 0 && _setupStep < SetupParts.Length)
                    Steps[3].Note = $"{n} of {SetupParts.Length}: {SetupParts[_setupStep]}";
                RebuildProgress();
            }
            return;
        }
        if (line.StartsWith("[progress] "))
        {
            if (double.TryParse(line[11..], NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            {
                _setupProgress = p;
                RebuildProgress();
            }
            return;
        }
        if (SetupOutput.Friendly(line) is { } shown)
            LastLine = shown;
    }

    private void RebuildProgress()
    {
        double within = 0;
        for (int i = 0; i < _setupStep && i < SetupWeights.Length; i++)
            within += SetupWeights[i];
        if (_setupStep >= 0 && _setupStep < SetupWeights.Length)
            within += SetupWeights[_setupStep] * _setupProgress;
        StepProgress(Math.Clamp(within, 0, 1));
    }

    private void FinishUpdate()
    {
        SetStep(4);
        Updater.Finish(_folder);
        Updater.Log(_folder, $"updated to {_toVersion}");
        foreach (var s in Steps)
            s.State = "done";
        Percent = 1;
        Stage = 2;
        Available = null;
        StaleVersion = null;
        DoneTitle = _afterCopy ? L.F("The game is rebuilt: version {0}", _toVersion) : L.F("Updated to version {0}", _toVersion);
        CheckText = L.F("Up to date (version {0}).", _toVersion);
        Mode = "done";
    }

    /// <summary>The rebuild failed: every file the update replaced goes back. When this program's own file was
    /// replaced, the old program is started again to say so (it reads the journal).</summary>
    private void FailRebuild(string why)
    {
        Updater.Log(_folder, "the rebuild failed: " + why);
        var journal = Updater.LoadJournal(_folder);
        if (journal == null)
        {
            Fail(new UpdateException("build", why), null);
            return;
        }
        var restart = Updater.RollBack(_folder, journal);
        journal.State = "failed";
        journal.FailKind = "build";
        journal.FailReason = why;
        if (restart)
        {
            Updater.SaveJournal(_folder, journal);
            Updater.Restart(_folder);
            _quit();
            return;
        }
        Updater.DeleteJournal(_folder);
        Fail(new UpdateException("build", why), journal);
    }

    private void Fail(UpdateException error, UpdateJournal? journal)
    {
        Updater.Log(_folder, $"the update stopped ({error.Kind}): {error.Message}");
        try { _cancel?.Cancel(); } catch { }
        ShowFailed(error.Kind, error.Message, journal?.From ?? CurrentVersion);
    }

    private void ShowFailed(string kind, string reason, string kept)
    {
        KeptVersion = _afterCopy
            ? L.T("Your game is as it was (the older build): Play still starts it, and your saves and settings work as before.")
            : L.F("You still have version {0}, exactly as it was. Your game, saves and settings work as before.", kept);
        FailTitle = kind == "busy" ? L.T("The update can't start yet") : L.T("The update couldn't finish");
        FailReason = reason;
        FailAdvice = kind switch
        {
            "download" => L.T("Check your internet connection and try again."),
            "damaged" => L.T("Try again: it downloads afresh."),
            "build" => L.T("Try again. If it keeps failing, open the update's log and post it in an issue on GitHub."),
            "busy" => "",
            "interrupted" => L.T("Try again when you're ready."),
            _ => L.T("Try again. If it keeps failing, open the update's log and post it in an issue on GitHub."),
        };
        foreach (var s in Steps.Where(s => s.State == "now"))
            s.State = "next";
        Mode = "failed";
    }

    [RelayCommand]
    private async Task TryAgain()
    {
        if (_afterCopy)
        {
            RebuildNow();
            return;
        }
        if (Available == null)
        {
            // after a restart the release isn't known yet: ask again
            Mode = "none";
            await CheckAsync(quiet: false);
            if (Available != null)
                await UpdateNow();
            return;
        }
        await UpdateNow();
    }

    // --- helpers ---

    private void SetStep(int index)
    {
        for (int i = 0; i < Steps.Count; i++)
            Steps[i].State = i < index ? "done" : i == index ? "now" : "next";
        _current = index;
        StepProgress(0);
    }

    private int _current;

    private void StepProgress(double within)
    {
        double before = 0;
        for (int i = 0; i < _current && i < Steps.Count; i++)
            before += Steps[i].Weight;
        Percent = Math.Clamp(before + Steps[Math.Clamp(_current, 0, Steps.Count - 1)].Weight * Math.Clamp(within, 0, 1), 0, 1);
        // a time left only while the game itself is built (its own pace), else how long the update has been running
        if (Percent < 1)
            TimeLine = SetupTime.Line(_clock.Elapsed, _current == 3 && _setupStep == 4, _setupProgress, _setupClock.Elapsed);
    }

    private void FillNotes(string notes)
    {
        NoteLines.Clear();
        foreach (var line in ParseNotes(notes))
            NoteLines.Add(line);
        if (NoteLines.Count == 0)
            NoteLines.Add(new NoteLine("No notes came with this version.", false, false));
    }

    /// <summary>A release's notes (GitHub's Markdown) as headings, points and text: ## / ### headings, - or * points,
    /// **bold** and `code` marks dropped, the version's own heading left out.</summary>
    public static List<NoteLine> ParseNotes(string notes)
    {
        var lines = new List<NoteLine>();
        foreach (var raw in notes.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("---") || line.StartsWith("<!--"))
                continue;
            if (line.StartsWith('#'))
            {
                var text = Clean(line.TrimStart('#'));
                if (raw.TrimStart().StartsWith("## ") && text.Length > 0 && char.IsDigit(text[0]))
                    continue; // "## 0.1.1 ...": the version, shown in the title already
                lines.Add(new NoteLine(text, true, false));
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                lines.Add(new NoteLine(Clean(line[2..]), false, true));
            else if (raw.StartsWith("  ") && lines.Count > 0 && lines[^1].IsBullet)
                lines[^1] = lines[^1] with { Text = lines[^1].Text + " " + Clean(line) }; // a point's next line
            else
                lines.Add(new NoteLine(Clean(line), false, false));
        }
        return lines;
    }

    private static string Clean(string text) => text.Replace("**", "").Replace("`", "").Trim();

    /// <summary>For pictures of the pages (--shot): a sample release and each page.</summary>
    /// <summary>Dev only (--selftest-progress): a rebuild's output fed through as if it ran, each change of what the
    /// page shows printed.</summary>
    public IEnumerable<string> ReplayRebuild(IEnumerable<string> lines)
    {
        _clock.Restart();
        Mode = "working";
        SetStep(3);
        Stage = 1;
        _setupStep = -1;
        Steps[3].Note = L.T("a few minutes");
        var last = "";
        foreach (var line in lines)
        {
            OnSetupLine(line);
            var shown = $"{Math.Round(Percent * 100),3}% | {Steps[3].Note} | {LastLine} | {TimeLine}";
            if (shown != last)
                yield return last = shown;
        }
    }
}
