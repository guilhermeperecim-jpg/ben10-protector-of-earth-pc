namespace Ben10Launcher.Services;

/// <summary>The first launch looks for the player's disc image by itself: .iso files in the user's own folders
/// (Downloads, Desktop, Documents, Videos), then on every fixed drive a few folders deep, skipping Windows' own folders.
/// Names that look like Ben 10's come first. Bounded in folders and time, so a big drive never holds the window up; the
/// player can always choose the file by hand.</summary>
public static class DiscFinder
{
    private const int UserDepth = 4;      // folders below Downloads, Desktop, …
    private const int DriveDepth = 3;     // folders below a drive's root
    private const int MaxFolders = 40_000;
    private const long MinBytes = 100L << 20;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(20);

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
        "AppData", "node_modules", ".git", "Recovery", "PerfLogs", "$WinREAgent", "MSOCache", "Intel", "AMD", "NVIDIA",
    };

    /// <summary>Every .iso found, the likeliest first.</summary>
    public static List<string> Find(CancellationToken cancel)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var folders = 0;

        void Walk(string root, int depth)
        {
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0 && !cancel.IsCancellationRequested && folders < MaxFolders && clock.Elapsed < Budget)
            {
                var (dir, d) = queue.Dequeue();
                if (!seen.Add(dir))
                    continue;
                folders++;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*.iso", Options))
                    {
                        try
                        {
                            if (new FileInfo(file).Length >= MinBytes)
                                found.Add(file);
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    if (d < depth)
                        foreach (var sub in Directory.EnumerateDirectories(dir, "*", Options))
                            if (!Skip.Contains(Path.GetFileName(sub)))
                                queue.Enqueue((sub, d + 1));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        foreach (var special in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                 })
            if (!string.IsNullOrEmpty(special) && Directory.Exists(special))
                Walk(special, UserDepth);
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    Walk(drive.RootDirectory.FullName, DriveDepth);
            }
            catch (IOException) { }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(Likeness).ToList();
    }

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>How much a file's name looks like the game's: checked first.</summary>
    private static int Likeness(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        var score = 0;
        if (name.Contains("ben10")) score += 4;
        if (name.Contains("protector")) score += 2;
        if (name.Contains("slus21661")) score += 4;
        return score;
    }
}
