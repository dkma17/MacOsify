namespace MacOSify.Win11.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MacOSifyWin11");

    public static string State { get; } = Path.Combine(Root, "State");
    public static string Journal { get; } = Path.Combine(State, "operation-journal.json");
    public static string JournalBackup { get; } = Path.Combine(State, "operation-journal.bak.json");
    public static string Logs { get; } = Path.Combine(Root, "Logs");
    public static string Cache { get; } = Path.Combine(Root, "Cache");
    public static string Staging { get; } = Path.Combine(Root, "Staging");
    public static string Recovery { get; } = Path.Combine(State, "Recovery");
    public static string Recipes { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Recipes");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(State);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Staging);
        Directory.CreateDirectory(Recovery);
    }
}
