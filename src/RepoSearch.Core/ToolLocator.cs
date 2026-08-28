using System.Diagnostics;

namespace RepoSearch.Core;

/// <summary>
/// Finds the external programs the actions shell out to. A packaged MSIX process does not
/// inherit the user's full PATH reliably, so each lookup falls back to known install paths.
/// </summary>
public static class ToolLocator
{
    private static readonly Lazy<string?> _vsCode = new(FindVSCode);
    private static readonly Lazy<string> _git = new(() => FindOnPath("git.exe") ?? "git");

    public static string Git => _git.Value;
    public static string? VSCode => _vsCode.Value;
    public static bool HasVSCode => _vsCode.Value is not null;

    private static string? FindVSCode()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] candidates =
        [
            Path.Combine(local, "Programs", "Microsoft VS Code", "bin", "code.cmd"),
            Path.Combine(programFiles, "Microsoft VS Code", "bin", "code.cmd"),
            Path.Combine(local, "Programs", "Microsoft VS Code Insiders", "bin", "code-insiders.cmd"),
        ];

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return FindOnPath("code.cmd");
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    /// <summary>Opens a URL, folder, or protocol handler with the shell.</summary>
    public static void OpenWithShell(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
