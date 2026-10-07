using JingTingYue.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace JingTingYue;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "静听阅";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RootFrame.Navigate(typeof(BookshelfPage));
        ApplyTitleBarTheme(JingTingYue.Services.AppTheme.Current);
    }

    public void ApplyTitleBarTheme(string theme)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var bar = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId).TitleBar;
            Windows.UI.Color Color(string key) => ((SolidColorBrush)App.Current.Resources[key]).Color;
            bar.BackgroundColor = Color("ChromeBgBrush");
            bar.ForegroundColor = Color("BookInkBrush");
            bar.InactiveBackgroundColor = Color("ChromeBgBrush");
            bar.InactiveForegroundColor = Color("MutedInkBrush");
            bar.ButtonBackgroundColor = Color("ChromeBgBrush");
            bar.ButtonForegroundColor = Color("BookInkBrush");
            bar.ButtonHoverBackgroundColor = Color("SelectedSageBrush");
            bar.ButtonHoverForegroundColor = Color("BookInkBrush");
            bar.ButtonPressedBackgroundColor = Color("ForestGreenBrush");
            bar.ButtonPressedForegroundColor = Color("OnAccentBrush");
            bar.ButtonInactiveBackgroundColor = Color("ChromeBgBrush");
            bar.ButtonInactiveForegroundColor = Color("MutedInkBrush");
        }
        catch { }
    }

    private bool _sizedOnce;
    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_sizedOnce) return;
        _sizedOnce = true;
        SizeWindowAndCenter(1280, 820);
    }

    private void SizeWindowAndCenter(int width, int height)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var scale = GetDpiForWindow(hwnd) / 96.0;
            var w = (int)(width * scale);
            var h = (int)(height * scale);
            appWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
            var area = Microsoft.UI.Windowing.DisplayArea
                .GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            var x = area.X + (area.Width - w) / 2;
            var y = area.Y + (area.Height - h) / 2;
            if (x < area.X) x = area.X;
            if (y < area.Y) y = area.Y;
            appWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
