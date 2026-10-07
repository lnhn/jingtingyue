using JingTingYue.Services;
using JingTingYue.Views;
using Microsoft.UI.Xaml;

namespace JingTingYue;

public partial class App : Application
{
    public static Window? CurrentWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppTheme.Load();
        await BookStore.LoadAsync();
        await NoteStore.LoadAsync();
        _ = TtsServer.EnsureStartedAsync();
        var window = new MainWindow();
        CurrentWindow = window;
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = AppTheme.Current == "night" ? ElementTheme.Dark : ElementTheme.Light;
        window.Activate();
        window.ApplyTitleBarTheme(AppTheme.Current);
    }
}
