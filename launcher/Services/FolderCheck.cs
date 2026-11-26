namespace Ben10Launcher.Services;

/// <summary>Whether the folder the launcher was unzipped to can build the game, and if not, why (in plain words), and
/// where it could go instead. What breaks the build: the launcher opened from inside the zip (Windows unpacks only the
/// exe to a temporary folder), OneDrive (it uploads and holds the build's files), a path too long for the build tools,
/// letters outside plain ASCII in the path (some build tools mishandle them: "C:\Users\João\..."), and a folder
/// Windows protects (Program Files, Windows) or that can't be written.</summary>
public static class FolderCheck
{
    // The build's deepest files lie ~140 characters below this folder, and the compiler can't write past Windows'
    // 260-character path limit.
    public const int MaxLength = 100;

    public sealed record Problem(string Title, string Detail);

    public static List<Problem> Problems(GameFolder folder)
    {
        var list = new List<Problem>();
        var root = folder.Root;
        // a developer's copy (the repository) is never checked
        if (folder.Data == folder.Root && File.Exists(folder.SetupScript))
            return list;
        var temp = Path.GetTempPath();
        if (root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !File.Exists(folder.SetupScript))
        {
            list.Add(new Problem(L.T("The launcher was opened from inside the zip"),
                L.T("Windows only unpacked the launcher itself, not the rest. Unzip the whole download first (right-click the zip > Extract All), then open the launcher from the folder you extracted.")));
            return list; // nothing else can be judged from a temporary folder
        }
        if (InOneDrive(root))
            list.Add(new Problem(L.T("This folder is inside OneDrive"),
                L.T("OneDrive would upload the build while it is made, and that stops it halfway.")));
        if (root.Length > MaxLength)
            list.Add(new Problem(L.T("This folder is too deep inside other folders"),
                L.F("Its path has {0} characters; the build tools need it under {1}.", root.Length, MaxLength)));
        if (root.Any(c => c > 127))
            list.Add(new Problem(L.T("The folder's path has accents or special letters"),
                L.T("Some of the build tools can't use paths with letters like á, ç or ã (often in the user's name).")));
        if (IsProtected(root) || !Writable(root))
            list.Add(new Problem(L.T("Windows protects this folder"),
                L.T("The launcher can't write here (Program Files and Windows are protected).")));
        return list;
    }

    public static bool InOneDrive(string root) =>
        new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable)
            .Any(d => !string.IsNullOrEmpty(d) && root.StartsWith(d.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) ||
        root.Contains(@"\OneDrive\", StringComparison.OrdinalIgnoreCase) ||
        root.Contains(@"\OneDrive -", StringComparison.OrdinalIgnoreCase);

    private static bool IsProtected(string root) =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        }.Any(p => !string.IsNullOrEmpty(p) && root.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    private static bool Writable(string root)
    {
        try
        {
            var probe = Path.Combine(root, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Where to move it: C:\Jogos\Ben 10 Recomp (the Windows drive), or a numbered one when that is taken.</summary>
    public static string Suggested()
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var target = Path.Combine(drive, L.Portuguese ? "Jogos" : "Games", "Ben 10 Recomp");
        for (var n = 2; Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any(); n++)
            target = Path.Combine(drive, L.Portuguese ? "Jogos" : "Games", $"Ben 10 Recomp ({n})");
        return target;
    }

    /// <summary>The install copied to target (the build's working folders and downloaded tools left out: made again
    /// when needed), reporting (files done, files in all). The old folder stays: the player deletes it.</summary>
    public static void CopyTo(string root, string target, Action<int, int> progress)
    {
        var skip = new[] { Path.Combine(root, "files", "build"), Path.Combine(root, "files", "tools") };
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !skip.Any(s => f.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        for (int i = 0; i < files.Count; i++)
        {
            var to = Path.Combine(target, Path.GetRelativePath(root, files[i]));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(files[i], to, overwrite: true);
            if (i % 50 == 0 || i == files.Count - 1)
                progress(i + 1, files.Count);
        }
    }
}
