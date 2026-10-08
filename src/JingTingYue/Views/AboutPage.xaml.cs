using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace JingTingYue.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
