using JingTingYue.Services;
using JingTingYue.Views;
using Microsoft.UI.Xaml;

namespace JingTingYue;

public partial class App : Application
{
    public static Window? CurrentWindow { get; private set; }

    public App()
    {
        if (DataPaths.IsPortable)
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(DataPaths.Root, "WebView2"));
        var bundledWebView = Path.Combine(AppContext.BaseDirectory, "WebView2Fixed");
        if (File.Exists(Path.Combine(bundledWebView, "msedgewebview2.exe")))
            Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", bundledWebView);
        InitializeComponent();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TtsServer.Stop();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppTheme.Load();
        await BookStore.LoadAsync();
        await NoteStore.LoadAsync();
        await SnippetStore.LoadAsync();
        _ = TtsServer.EnsureStartedAsync();
        var window = new MainWindow();
        CurrentWindow = window;
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = AppTheme.Current == "night" ? ElementTheme.Dark : ElementTheme.Light;
        window.Activate();
        window.ApplyTitleBarTheme(AppTheme.Current);
    }
}
