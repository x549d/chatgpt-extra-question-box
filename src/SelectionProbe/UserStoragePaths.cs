using System.IO;

namespace ContextNotes.SelectionProbe;

// LocalAppData can have different MSIX views in packaged launchers and desktop
// children. Keep this application's durable store outside that virtualized tree.
internal static class UserStoragePaths
{
    public static string Root => RootFor(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    public static string HistoryPath => ResolveHistoryPath(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), File.Exists);

    internal static string RootFor(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile))
            throw new InvalidOperationException("A verified Windows user profile is required for local storage.");
        return Path.Combine(Path.GetFullPath(profile), ".context-notes-native");
    }

    internal static string ResolveHistoryPath(string profile, string localAppData, Func<string, bool> exists)
    {
        string current = Path.Combine(RootFor(profile), "data", "history.db");
        if (exists(current)) return current;
        string realLocal = Path.Combine(Path.GetFullPath(profile), "AppData", "Local");
        var candidates = new[]
        {
            Path.Combine(realLocal, "ContextNotesNative", "data", "history.db"),
            Path.Combine(realLocal, "Packages", "OpenAI.Codex_2p2nqsd0c76g0", "LocalCache", "Local", "ContextNotesNative", "data", "history.db"),
            string.IsNullOrWhiteSpace(localAppData) ? current : Path.Combine(localAppData, "ContextNotesNative", "data", "history.db")
        };
        if (candidates.Any(exists))
            throw new InvalidOperationException("Legacy history exists. Export and verify a consistent backup, then use Manage-Installation -LegacyHistoryBackup before starting this version. Original databases are preserved.");
        return current;
    }
}
