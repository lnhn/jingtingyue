namespace JingTingYue.Services;

public static class DataPaths
{
    public static bool IsPortable => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JINGTINGYUE_DATA_DIR"))
        || File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        var portable = Environment.GetEnvironmentVariable("JINGTINGYUE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(portable))
            return Path.GetFullPath(portable);
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")))
            return AppContext.BaseDirectory;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JingTingYue");
    }
}
