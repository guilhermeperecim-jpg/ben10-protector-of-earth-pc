using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ben10Launcher.Services;

namespace Ben10Launcher.ViewModels;

/// <summary>The launcher's window: the home page (PLAY and the page list), Options, Controls and Game files; the first
/// launch's setup and an update's pages in the window's place. Adapted from def-jam-vendetta-recomp's settings program
/// (GPL-3.0).</summary>
public partial class MainViewModel : ObservableObject
{
    public GameFolder Folder { get; }
    public SettingsViewModel Settings { get; }
    public SetupViewModel Setup { get; }
    public UpdateViewModel Updates { get; }

    public MainViewModel(GameFolder folder)
    {
        Folder = folder;
        Settings = new SettingsViewModel(folder);
        Setup = new SetupViewModel(folder, FinishSetup);
        Updates = new UpdateViewModel(folder, () => Settings.CheckUpdates, Exit);
        InSetup = folder.NeedsSetup;
        FolderProblems = FolderCheck.Problems(folder);
        MoveTarget = FolderCheck.Suggested();
        Updates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UpdateViewModel.Mode) or nameof(UpdateViewModel.ShowNotice))
            {
                OnPropertyChanged(nameof(InUpdate));
                OnPropertyChanged(nameof(OnHome));
                OnPropertyChanged(nameof(OnPage));
                OnPropertyChanged(nameof(CanPlay));
                OnPropertyChanged(nameof(UpdateNotice));
                OnPropertyChanged(nameof(Status));
                if (e.PropertyName == nameof(UpdateViewModel.Mode) && !Updates.Active)
                    OnPropertyChanged(string.Empty); // after an update: the version and the game read again
            }
        };
        Setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SetupViewModel.Done) or nameof(SetupViewModel.Error) or nameof(SetupViewModel.Page))
            {
                OnPropertyChanged(nameof(CanPlay));
                OnPropertyChanged(nameof(Status));
            }
        };
        // whether the game is running, however it was started: checked each second (two copies would share the card)
        GameRunning = GameFolder.IsGameRunning();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => GameRunning = GameFolder.IsGameRunning();
        timer.Start();
    }

    // --- pages: 0 home, 1 options, 2 controls, 3 game files ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnHome), nameof(OnPage), nameof(IsOptions), nameof(IsControls), nameof(IsGameFiles),
        nameof(PageTitle))]
    private int _page;

    /// <summary>First run (no game built yet): the setup in the window's place.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnHome), nameof(OnPage), nameof(CanPlay), nameof(Status), nameof(FolderBlocked),
        nameof(ShowSetup))]
    private bool _inSetup;

    public bool InUpdate => Updates.Active;

    // --- a folder the game can't be built in (FolderCheck): its own page, with a button that moves the install ---
    public List<FolderCheck.Problem> FolderProblems { get; }
    /// <summary>Only before the game is built: a built game plays wherever it is.</summary>
    public bool FolderBlocked => InSetup && FolderProblems.Count > 0;
    public bool ShowSetup => InSetup && !FolderBlocked;
    public bool CanMove => !FolderProblems.Any(p => p.Title == L.T("The launcher was opened from inside the zip"));
    public string MoveTarget { get; }
    public string MoveButtonText => L.F("Move it to {0}", MoveTarget);

    [ObservableProperty] private bool _moving;
    [ObservableProperty] private string _moveText = "";

    [RelayCommand]
    private async Task MoveFolder()
    {
        if (Moving)
            return;
        Moving = true;
        var target = MoveTarget;
        try
        {
            await Task.Run(() => FolderCheck.CopyTo(Folder.Root, target, (done, all) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => MoveText = L.F("Copying: {0} of {1} files", done, all))));
            var launcher = Path.Combine(target, GameFolder.LauncherName + ".exe");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(launcher) { WorkingDirectory = target, UseShellExecute = true });
            Exit();
        }
        catch (Exception ex)
        {
            MoveText = L.F("Couldn't move it: {0}", ex.Message);
            Moving = false;
        }
    }
    public bool OnHome => !InSetup && !InUpdate && Page == 0;
    public bool OnPage => !InSetup && !InUpdate && Page != 0;
    public bool IsOptions => Page == 1;
    public bool IsControls => Page == 2;
    public bool IsGameFiles => Page == 3;
    public string PageTitle => Page switch { 1 => L.T("Options"), 2 => L.T("Controls"), 3 => L.T("Game files"), _ => "" };

    [RelayCommand] private void ShowOptions() => Page = 1;
    [RelayCommand] private void ShowControls() => Page = 2;
    [RelayCommand] private void ShowGameFiles() { Page = 3; OnPropertyChanged(nameof(GameBuiltText)); }
    [RelayCommand] private void ShowHome() { Page = 0; Settings.Note = ""; }

    private void FinishSetup()
    {
        InSetup = false;
        Page = 0;
        OnPropertyChanged(string.Empty);
    }

    // --- PLAY and the status line under the picture ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay), nameof(Status))]
    private bool _gameRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(StatusGood))]
    private string _message = "";

    public bool CanPlay => Folder.HasGame && Folder.GameExe != null && !GameRunning && !InSetup && !Updates.IsWorking;

    public string Status =>
        Message.Length > 0 ? Message
        : GameRunning ? L.T("The game is running")
        : CanPlay ? L.T("Ben 10: Protector of Earth - ready to play")
        : L.T("The game isn't built yet");
    public bool StatusGood => Message.Length == 0 && CanPlay;

    [RelayCommand]
    private async Task Play()
    {
        if (Folder.Play(out var game) is { } error)
        {
            Message = error;
            return;
        }
        Message = L.T("Starting the game…");
        GameRunning = true;
        var closed = game!.WaitForExitAsync();
        await Task.WhenAny(closed, Task.Delay(TimeSpan.FromSeconds(6)));
        Message = closed.IsCompleted && game.ExitCode != 0 ? L.T("The game closed while starting: see logs\\game.log.err") : "";
    }

    // --- the update notice at the bottom right ---
    public bool UpdateNotice => Updates.ShowNotice;
    public string UpdateNoticeText => L.F("Update available: {0}", Updates.NewVersion);
    public string Version => "v" + Updater.CurrentText;

    // --- Game files ---
    public string DiscText => File.Exists(Folder.DiscPath) && Folder.HasGame
        ? L.T("Checked: Ben 10: Protector of Earth, USA, version 1.01 (SLUS-21661)") : L.T("Not read yet");
    public string DiscFile => Folder.DiscPath;
    public string GameBuiltText => Folder.GameExe is { } exe
        ? L.F("Built: {0}", File.GetLastWriteTime(exe).ToString("g")) : L.T("Not built yet");

    [RelayCommand] private void OpenFolder() => GameFolder.Open(Folder.Root);
    [RelayCommand] private void OpenSaves() => GameFolder.Open(Folder.SavesPath);
    [RelayCommand] private void OpenLogs() => GameFolder.Open(Folder.LogsPath);

    /// <summary>The setup again from its build page: the disc already read, the game built afresh where it changed.</summary>
    [RelayCommand]
    private void Rebuild()
    {
        InSetup = true;
        Setup.BuildCommand.Execute(null);
    }

    [RelayCommand]
    private void ChooseDisc()
    {
        InSetup = true;
        Setup.Page = 1;
    }

    [RelayCommand]
    private void Exit() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    /// <summary>When the window has opened: an update left halfway, or a check for a new one.</summary>
    public Task OnOpenedAsync()
    {
        // the first launch: the disc looked for at once (the player only checks it and presses Next)
        if (ShowSetup && !Folder.HasGame && Setup.DiscPath == null)
            _ = Setup.ScanAsync();
        return Updates.StartAsync(InSetup);
    }

    /// <summary>For pictures (--shot): home, options, controls, files, setup.</summary>
    public void ShowPage(string page)
    {
        switch (page)
        {
            case "options": Page = 1; break;
            case "options-launcher": Page = 1; Settings.TabIndex = 3; break;
            case "controls": Page = 2; break;
            case "files": Page = 3; break;
            case "setup": InSetup = true; break;
            case "folder": InSetup = true; FolderProblems.Clear(); FolderProblems.Add(new FolderCheck.Problem(
                L.T("This folder is inside OneDrive"), L.T("OneDrive would upload the build while it is made, and that stops it halfway.")));
                FolderProblems.Add(new FolderCheck.Problem(L.T("The folder's path has accents or special letters"),
                L.T("Some of the build tools can't use paths with letters like á, ç or ã (often in the user's name).")));
                OnPropertyChanged(string.Empty); break;
            case "setup-found": InSetup = true; Setup.ShowSample(); break;
            case "setup-tools": InSetup = true; Setup.ShowToolsSample(false); break;
            case "setup-installer": InSetup = true; Setup.ShowToolsSample(true); break;
            default: InSetup = false; Page = 0; break;
        }
    }
}
