using System.Diagnostics;

namespace FocusLock;

/// <summary>
/// Does the actual blocking:
///  - closes blocked apps every couple of seconds while a session runs
///  - redirects blocked websites to 0.0.0.0 through the Windows hosts file
/// </summary>
public static class Blocker
{
    private const string StartMarker = "# >>> FocusLock start (removed automatically when the session ends)";
    private const string EndMarker = "# <<< FocusLock end";

    /// <summary>
    /// Anything containing one of these words is never closed or blocked, even if it is
    /// typed into a block list: Claude (app, website and API), TryHackMe, and your lab tools.
    /// </summary>
    private static readonly string[] ProtectedKeywords =
    {
        "claude", "anthropic", "tryhackme", "vmware", "virtualbox", "kali"
    };

    /// <summary>Process names that must never be killed.</summary>
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FocusLock", "explorer", "Code", "dwm", "csrss", "winlogon", "svchost",
        "chrome", "msedge", "firefox", "brave", "opera", "obsidian", "vmx", "vmplayer"
    };

    public static bool IsProtected(string entry)
    {
        var e = entry.Trim().ToLowerInvariant();
        if (e.EndsWith(".exe")) e = e[..^4];
        return ProtectedKeywords.Any(k => e.Contains(k)) || ProtectedProcesses.Contains(e);
    }

    private static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    /// <summary>Kills every running process whose name is in the list. Returns the names it closed.</summary>
    public static List<string> CloseBlockedApps(IEnumerable<string> blockedApps)
    {
        var closed = new List<string>();
        var self = Environment.ProcessId;

        foreach (var name in blockedApps)
        {
            var clean = name.Trim();
            if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                clean = clean[..^4];
            if (clean.Length == 0 || IsProtected(clean)) continue;

            foreach (var p in Process.GetProcessesByName(clean))
            {
                try
                {
                    if (p.Id == self) continue;
                    p.Kill(entireProcessTree: true);
                    closed.Add(p.ProcessName);
                }
                catch { /* already gone, or access denied */ }
                finally { p.Dispose(); }
            }
        }
        return closed;
    }

    /// <summary>Adds the blocked sites to the hosts file. Returns false if it could not (not admin).</summary>
    public static bool BlockSites(IEnumerable<string> sites, out string error)
    {
        error = "";
        try
        {
            var lines = RemoveOurBlock(File.ReadAllLines(HostsPath)).ToList();

            lines.Add(StartMarker);
            foreach (var raw in sites)
            {
                var site = raw.Trim().ToLowerInvariant()
                              .Replace("https://", "").Replace("http://", "").TrimEnd('/');
                if (site.StartsWith("www.")) site = site[4..];
                if (site.Length == 0 || IsProtected(site)) continue;

                lines.Add($"0.0.0.0 {site}");
                lines.Add($"0.0.0.0 www.{site}");
                lines.Add($"0.0.0.0 m.{site}");
            }
            lines.Add(EndMarker);

            File.WriteAllLines(HostsPath, lines);
            FlushDns();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Removes everything FocusLock added to the hosts file.</summary>
    public static void UnblockSites()
    {
        try
        {
            var original = File.ReadAllLines(HostsPath);
            var cleaned = RemoveOurBlock(original).ToList();
            if (cleaned.Count != original.Length)
            {
                File.WriteAllLines(HostsPath, cleaned);
                FlushDns();
            }
        }
        catch { /* not admin - nothing we can do */ }
    }

    private static IEnumerable<string> RemoveOurBlock(IEnumerable<string> lines)
    {
        bool inside = false;
        foreach (var line in lines)
        {
            if (line.StartsWith(StartMarker)) { inside = true; continue; }
            if (line.StartsWith(EndMarker)) { inside = false; continue; }
            if (!inside) yield return line;
        }
    }

    private static void FlushDns()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            p?.WaitForExit(5000);
        }
        catch { }
    }
}
