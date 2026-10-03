namespace Soluna.Shared;

public static class DataPaths
{
    /// <summary>
    /// The repository root (the folder holding Soluna.slnx), found by walking up from the
    /// executable, so data/ and assets/ resolve the same under dotnet run and a published build.
    /// Falls back to the executable folder.
    /// </summary>
    public static string Root { get; } = FindRoot();

    public static string Data => Path.Combine(Root, "data");

    public static string Assets => Path.Combine(Root, "assets");

    private static string FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("SOLUNA_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Soluna.slnx"))) return dir.FullName;
        }
        return AppContext.BaseDirectory;
    }
}
