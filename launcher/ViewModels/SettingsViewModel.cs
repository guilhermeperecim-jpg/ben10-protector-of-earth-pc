using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ben10Launcher.Services;

namespace Ben10Launcher.ViewModels;

/// <summary>The Options and Controls pages: settings.toml's values, saved as they change (only the lines that changed:
/// comments and hand edits stay), and the launcher's own language (launcher.toml).</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly GameFolder _folder;
    private GameSettings _saved;   // what the file holds now (only what changed since is written)
    private bool _loading;

    public SettingsViewModel(GameFolder folder)
    {
        _folder = folder;
        _saved = GameSettings.Load(folder.SettingsPath);
        Screens = Displays.Find().Select(s => $"{s.Number}: {s.Current.Width} x {s.Current.Height}" + (s.Primary ? " ★" : "")).ToList();
        if (Screens.Count == 0)
            Screens.Add("1");
        Load(_saved);
    }

    public List<string> Screens { get; }

    // --- the choices, in the order the pages list them ---
    private static readonly string[] Modes = { "borderless", "window" };
    private static readonly string[] Shapes = { "widescreen", "4:3", "stretch" };
    private static readonly string[] Renderers = { "gpu", "cpu" };
    private static readonly int[] ScaleValues = { 1, 2, 4, 8, 16 };
    private static readonly string[] Filters = { "fsr", "smooth", "blocky" };
    private static readonly string[] Languages = { "auto", "pt", "en" };

    public List<string> ModeNames { get; } = new() { L.T("Borderless fullscreen"), L.T("Window") };
    public List<string> ShapeNames { get; } = new()
    {
        L.T("Widescreen 16:9 (levels; menus and movies stay 4:3)"), L.T("4:3, as on the PS2"), L.T("Stretch to the window"),
    };
    public List<string> RendererNames { get; } = new()
    {
        L.T("Graphics card (recommended)"), L.T("Processor (slower, the PS2's own resolution)"),
    };
    public List<string> ScaleNames { get; } = ScaleValues.Select(n => L.F("{0}x the PS2's detail", n)).ToList();
    public List<string> FilterNames { get; } = new() { L.T("FSR (sharp)"), L.T("Smooth"), L.T("Blocky (pixels)") };
    public List<string> LanguageNames { get; } = new() { L.T("Automatic (as Windows)"), "Português", "English" };

    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private int _screenIndex;
    [ObservableProperty] private bool _maximised;
    [ObservableProperty] private bool _vsync;
    [ObservableProperty] private int _shapeIndex;
    [ObservableProperty] private int _rendererIndex;
    [ObservableProperty] private int _scaleIndex;
    [ObservableProperty] private int _filterIndex;
    [ObservableProperty] private double _sharpening;
    [ObservableProperty] private bool _sharp2D;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _reverb;
    [ObservableProperty] private bool _keyboard;
    [ObservableProperty] private bool _vibration;
    [ObservableProperty] private bool _checkUpdates;
    [ObservableProperty] private int _languageIndex;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private int _tabIndex; // Display, Graphics, Sound, Launcher

    public bool IsWindow => ModeIndex == 1;
    public bool IsGpu => RendererIndex == 0;
    public bool IsFsr => FilterIndex == 0 && IsGpu;
    public string VolumeText => $"{(int)Volume}%";
    public string SharpeningText => $"{(int)Sharpening}";
    public int ControllersFound => Controllers.XInputCount();
    public string ControllersText => L.F("Controllers found now: {0}", ControllersFound);
    public string Hardware => Machine.Summary();

    private void Load(GameSettings s)
    {
        _loading = true;
        ModeIndex = Math.Max(0, Array.IndexOf(Modes, s.Mode));
        ScreenIndex = Math.Clamp(s.Screen - 1, 0, Screens.Count - 1);
        Maximised = s.Maximised;
        Vsync = s.Vsync;
        ShapeIndex = Math.Max(0, Array.IndexOf(Shapes, s.Shape));
        RendererIndex = Math.Max(0, Array.IndexOf(Renderers, s.Renderer));
        ScaleIndex = Math.Max(0, Array.IndexOf(ScaleValues, s.Scale));
        FilterIndex = Math.Max(0, Array.IndexOf(Filters, s.Filter));
        Sharpening = s.Sharpening;
        Sharp2D = s.Sharp2D;
        Volume = s.Volume;
        Reverb = s.Reverb;
        Keyboard = s.Keyboard != 0;
        Vibration = s.Vibration;
        CheckUpdates = s.CheckUpdates;
        LanguageIndex = Math.Max(0, Array.IndexOf(Languages, L.Setting));
        _loading = false;
    }

    private GameSettings Current() => new()
    {
        Mode = Modes[Math.Clamp(ModeIndex, 0, Modes.Length - 1)],
        Screen = ScreenIndex + 1,
        Maximised = Maximised,
        Vsync = Vsync,
        Shape = Shapes[Math.Clamp(ShapeIndex, 0, Shapes.Length - 1)],
        Renderer = Renderers[Math.Clamp(RendererIndex, 0, Renderers.Length - 1)],
        Scale = ScaleValues[Math.Clamp(ScaleIndex, 0, ScaleValues.Length - 1)],
        Filter = Filters[Math.Clamp(FilterIndex, 0, Filters.Length - 1)],
        Sharpening = (int)Math.Round(Sharpening),
        Sharp2D = Sharp2D,
        Volume = (int)Math.Round(Volume),
        Reverb = Reverb,
        Keyboard = Keyboard ? 1 : 0,
        Vibration = Vibration,
        CheckUpdates = CheckUpdates,
    };

    /// <summary>Any value changed on a page: written to settings.toml at once (the game reads it when it opens).</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(ModeIndex): OnPropertyChanged(nameof(IsWindow)); break;
            case nameof(RendererIndex): OnPropertyChanged(nameof(IsGpu)); OnPropertyChanged(nameof(IsFsr)); break;
            case nameof(FilterIndex): OnPropertyChanged(nameof(IsFsr)); break;
            case nameof(Volume): OnPropertyChanged(nameof(VolumeText)); break;
            case nameof(Sharpening): OnPropertyChanged(nameof(SharpeningText)); break;
        }
        if (_loading || e.PropertyName is nameof(Note) or nameof(TabIndex) or nameof(HasShortcutPicture) or
                nameof(ShortcutPicture) or nameof(IsWindow) or nameof(IsGpu) or nameof(IsFsr) or
                nameof(VolumeText) or nameof(SharpeningText))
            return;
        if (e.PropertyName == nameof(LanguageIndex))
        {
            L.Save(_folder, Languages[Math.Clamp(LanguageIndex, 0, Languages.Length - 1)]);
            Note = L.T("Language changes show when you go back.");
            LanguageChanged?.Invoke();
            return;
        }
        Save();
    }

    /// <summary>The language was changed: the window is made again in it (MainWindow).</summary>
    public event Action? LanguageChanged;

    private void Save()
    {
        try
        {
            var now = Current();
            now.Save(_folder.SettingsPath, _saved);
            _saved = now;
        }
        catch (IOException ex)
        {
            Note = ex.Message;
        }
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        var defaults = new GameSettings();
        Load(defaults);
        Save();
    }

    [RelayCommand]
    private void MakeShortcuts() => Note = _folder.CreateGameShortcut() ?? L.T("The game's shortcut is on your desktop.");

    // --- the picture of the game's shortcut (the player's own: nothing of it comes with the launcher) ---
    public bool HasShortcutPicture => File.Exists(_folder.ShortcutIconPath);
    public Avalonia.Media.Imaging.Bitmap? ShortcutPicture =>
        HasShortcutPicture ? LoadPicture(_folder.ShortcutIconPath) : null;

    private static Avalonia.Media.Imaging.Bitmap? LoadPicture(string ico)
    {
        try
        {
            // the icon's largest picture (the first one IconMaker writes: 256 px PNG)
            var data = File.ReadAllBytes(ico);
            int size = BitConverter.ToInt32(data, 6 + 8), offset = BitConverter.ToInt32(data, 6 + 12);
            return new Avalonia.Media.Imaging.Bitmap(new MemoryStream(data, offset, size));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A picture chosen (OptionsView's file picker): made into the shortcut's icon, and the shortcut made again
    /// with it when the game is built.</summary>
    public void SetShortcutPicture(string picture)
    {
        try
        {
            IconMaker.Make(picture, _folder.ShortcutIconPath);
        }
        catch (Exception ex)
        {
            Note = L.F("That picture couldn't be used: {0}", ex.Message);
            return;
        }
        OnPropertyChanged(nameof(HasShortcutPicture));
        OnPropertyChanged(nameof(ShortcutPicture));
        Note = _folder.GameExe == null ? L.T("Picture saved: the shortcut gets it once the game is built.")
            : _folder.CreateGameShortcut() ?? L.T("The game's shortcut is on your desktop, with your picture.");
    }

    [RelayCommand]
    private void DefaultShortcutPicture()
    {
        try { File.Delete(_folder.ShortcutIconPath); } catch (IOException) { }
        OnPropertyChanged(nameof(HasShortcutPicture));
        OnPropertyChanged(nameof(ShortcutPicture));
        if (_folder.GameExe != null)
            Note = _folder.CreateGameShortcut() ?? L.T("The game's shortcut is on your desktop.");
    }
}

/// <summary>The line about this PC under the pages: the graphics card (and its memory), the processor's threads and the
/// memory, as Windows' registry and memory status give them.</summary>
public static class Machine
{
    /// <summary>The README's pictures: this PC's parts not shown.</summary>
    public static bool Hidden { get; set; }

    public static string Summary()
    {
        if (Hidden)
            return "";
        var threads = Environment.ProcessorCount;
        var ram = Math.Round(TotalMemoryGb());
        var (gpu, vram) = GraphicsCard();
        return vram > 0
            ? L.F("{0} ({1} GB), {2} CPU threads, {3} GB RAM", gpu, vram, threads, ram)
            : L.F("{0}, {1} CPU threads, {2} GB RAM", gpu, threads, ram);
    }

    private static (string Name, double MemoryGb) GraphicsCard()
    {
        if (!OperatingSystem.IsWindows())
            return ("?", 0);
        const string Class = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        (string Name, double MemoryGb) best = ("?", 0);
        try
        {
            using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Class);
            foreach (var sub in root?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var key = root!.OpenSubKey(sub);
                if (key?.GetValue("DriverDesc") is not string name)
                    continue;
                var bytes = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => 0L,
                };
                var gb = Math.Round(bytes / (double)(1L << 30));
                if (gb > best.MemoryGb || best.Name == "?")
                    best = (name, gb);
            }
        }
        catch (Exception)
        {
        }
        return best;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong Total, Free, PageTotal, PageFree, VirtualTotal, VirtualFree, Extended;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private static double TotalMemoryGb()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return OperatingSystem.IsWindows() && GlobalMemoryStatusEx(ref status) ? status.Total / (double)(1L << 30) : 0;
    }
}
