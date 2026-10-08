using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace JingTingYue.Services;

public static class AppTheme
{
    private static readonly string ThemeFile = Path.Combine(DataPaths.Root, "theme.txt");

    public static string Current { get; private set; } = "paper";

    public static void Load()
    {
        try { Set(File.ReadAllText(ThemeFile).Trim(), false); }
        catch { Set("paper", false); }
    }

    public static void Set(string theme, bool save = true)
    {
        if (theme is not ("paper" or "sepia" or "night")) theme = "paper";
        Current = theme;
        var colors = theme switch
        {
            "sepia" => new[] { "#F5EAD7", "#FBF2E3", "#EDE0CB", "#30271F", "#756A5E", "#A74730", "#EAD3B9", "#DFCFB8", "#EEE1CE", "#FFFFFF", "#993D2D", "#A74730", "#833725" },
            "night" => new[] { "#202126", "#292A30", "#27282E", "#F3EFE9", "#BBB7B1", "#F0A080", "#45362F", "#414248", "#35363D", "#25201D", "#F2A699", "#F4B094", "#D98769" },
            _ => new[] { "#F8F7F4", "#FFFFFF", "#F2F0EC", "#252320", "#75716B", "#B84C35", "#FBE6DF", "#E7E3DE", "#F5F3EE", "#FFFFFF", "#A33E2E", "#A74730", "#913C2B" },
        };
        var keys = new[] { "MainBgBrush", "ChromeBgBrush", "SidebarBgBrush", "BookInkBrush",
            "MutedInkBrush", "ForestGreenBrush", "SelectedSageBrush", "HairlineBrush",
            "NoteCardBrush", "OnAccentBrush", "DangerBrush", "TerracottaBrush", "ForestGreenDeepBrush" };
        for (int i = 0; i < keys.Length; i++)
            ((SolidColorBrush)Application.Current.Resources[keys[i]]).Color = Parse(colors[i]);
        if (App.CurrentWindow?.Content is FrameworkElement root)
            root.RequestedTheme = theme == "night" ? ElementTheme.Dark : ElementTheme.Light;
        if (App.CurrentWindow is MainWindow window)
            window.ApplyTitleBarTheme(theme);
        if (save)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ThemeFile)!);
            File.WriteAllText(ThemeFile, theme);
        }
    }

    private static Color Parse(string hex) => Color.FromArgb(255,
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16));
}
