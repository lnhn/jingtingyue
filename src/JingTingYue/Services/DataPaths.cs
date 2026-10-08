namespace JingTingYue.Services;

public static class DataPaths
{
    public static bool IsPortable => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JINGTINGYUE_DATA_DIR"));
    public static string Root { get; } = ResolveRoot();

    private static string ResolveRoot()
    {
        var portable = Environment.GetEnvironmentVariable("JINGTINGYUE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(portable))
            return Path.GetFullPath(portable);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JingTingYue");
    }
}
