using System.IO;
using ContextNotes.SelectionProbe;
using Xunit;

namespace SelectionProbe.Tests;

public sealed class Phase6StoragePathTests
{
    private static readonly string Profile = Path.Combine(Path.GetTempPath(), "context-profile-fixture");
    [Fact] public void PackagedAndDesktopLocalAppDataViewsResolveTheSameFreshStore()
    {
        string desktop = Path.Combine(Profile, "AppData", "Local");
        string packaged = Path.Combine(desktop, "Packages", "OpenAI.Codex_fixture", "LocalCache", "Local");
        string first = UserStoragePaths.ResolveHistoryPath(Profile, desktop, _ => false);
        Assert.Equal(first, UserStoragePaths.ResolveHistoryPath(Profile, packaged, _ => false));
        Assert.StartsWith(Path.Combine(Profile, ".context-notes-native"), first);
        Assert.False(first.StartsWith(desktop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void LegacyDataPreventsSilentlyCreatingAnEmptyReplacement()
    {
        string local = Path.Combine(Profile, "AppData", "Local");
        string legacy = Path.Combine(local, "ContextNotesNative", "data", "history.db");
        Assert.Throws<InvalidOperationException>(() => UserStoragePaths.ResolveHistoryPath(Profile, local, p => p == legacy));
    }
    [Fact] public void VerifiedNewStoreIsSelectedWithoutChangingLegacyFiles()
    {
        string expected = Path.Combine(Profile, ".context-notes-native", "data", "history.db");
        Assert.Equal(expected, UserStoragePaths.ResolveHistoryPath(Profile, Path.Combine(Profile, "AppData", "Local"), _ => true));
    }
    [Fact] public void MissingProfileCannotFallBackToWorkingDirectory()
    {
        Assert.Throws<InvalidOperationException>(() => UserStoragePaths.RootFor(""));
        Assert.Throws<InvalidOperationException>(() => UserStoragePaths.RootFor("relative-profile"));
    }
}
